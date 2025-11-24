using HtmlAgilityPack;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace GonePhishing.Services
{
    public class HtmlAnalysisResult
    {
        public string Url { get; set; }
        public string Html { get; set; }
        public string Title { get; set; }

        public bool HasCredentialForm { get; set; }
        public bool FormPostsToThirdParty { get; set; }
        public bool ObfuscatedJS {  get; set; }
        public bool HiddenIframe { get; set; }
        public bool OAuthRedirect { get; set; }

        public List<string> Images { get; set; } = new();
        public string RedirectLocation { get; set; }

        public int RiskScore { get; set; }

        public string Reasons { get; set; }
    }

    public class HtmlService
    {
        private readonly HttpClient _client;

        public HtmlService()
        {
            _client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false
            })
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        public async Task<HtmlAnalysisResult> AnalyzeAsync(string url, string baseDomain = null)
        {
            var result = new HtmlAnalysisResult
            {
                Url = url,
            };

            HttpResponseMessage response = null;
            string currentUrl = url;
            string lastRedirect = null;
            int maxRedirects = 7;

            for (int i = 0; i < maxRedirects; i++)
            {
                response = await _client.GetAsync(currentUrl);

                if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                {
                    var location = response.Headers.Location;
                    if (location == null)
                        break;

                    currentUrl = location.IsAbsoluteUri ? location.ToString() : new Uri(new Uri(currentUrl), location).ToString();
                    lastRedirect = currentUrl; // track redirect URL

                    if (Uri.TryCreate(currentUrl, UriKind.Absolute, out var redirectUri))
                    {
                        DetectUnexpectedOAuth(result, redirectUri.Host, baseDomain);
                    }

                    continue;
                }

                // final page reached
                result.Html = await response.Content.ReadAsStringAsync();
                break;
            }

            // Set the redirect location if there was any redirect
            if (lastRedirect != null)
                result.RedirectLocation = lastRedirect;

            // If HTML is empty, stop early
            if (string.IsNullOrWhiteSpace(result.Html))
                return result;

            // Parse HTML DOM
            var doc = new HtmlDocument();
            doc.LoadHtml(result.Html);

            ExtractTitle(result, doc);
            ExtractForms(result, doc, baseDomain);
            ExtractImages(result, doc);
            DetectHiddenIframes(result, doc);
            DetectSuspiciousJavaScript(result, doc);

            // Compute final risk score
            ComputeRiskScore(result, baseDomain);

            return result;
        }


        // ------------------------------------------------------------------
        // Extract title
        // ------------------------------------------------------------------
        private void ExtractTitle(HtmlAnalysisResult result, HtmlDocument doc)
        {
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            result.Title = titleNode?.InnerText?.Trim() ?? "";
        }

        // ------------------------------------------------------------------
        // Extract forms + detect credential capture
        // ------------------------------------------------------------------
        private void ExtractForms(HtmlAnalysisResult result, HtmlDocument doc, string baseDomain)
        {
            var forms = doc.DocumentNode.SelectNodes("//form");
            if (forms == null)
                return;

            foreach (var form in forms)
            {
                var action = form.GetAttributeValue("action", "")?.Trim() ?? "";

                var inputs = form.SelectNodes(".//input") ?? new HtmlNodeCollection(null);
                bool hasPassword = inputs.Any(i =>
                    i.GetAttributeValue("type", "").Equals("password", StringComparison.OrdinalIgnoreCase)
                );

                bool hasUserField = inputs.Any(i =>
                {
                    var type = i.GetAttributeValue("type", "").ToLower();
                    var name = i.GetAttributeValue("name", "").ToLower();
                    return type == "text" && (name.Contains("user") || name.Contains("email"));
                });

                if (hasPassword && hasUserField)
                    result.HasCredentialForm = true;

                // If action posts to an external domain
                if (!string.IsNullOrWhiteSpace(action) &&
                    baseDomain != null &&
                    Uri.TryCreate(action, UriKind.RelativeOrAbsolute, out var uri))
                {
                    string actionHost;

                    if (!uri.IsAbsoluteUri)
                    {
                        // Convert relative to full URL
                        var baseUri = new Uri(result.Url);
                        actionHost = new Uri(baseUri, uri).Host;
                    }
                    else
                    {
                        actionHost = uri.Host;
                    }

                    if (!actionHost.EndsWith(baseDomain, StringComparison.OrdinalIgnoreCase))
                        result.FormPostsToThirdParty = true;
                }
            }
        }

        // ------------------------------------------------------------------
        // Suspicious JavaScript
        // ------------------------------------------------------------------
        private void DetectSuspiciousJavaScript(HtmlAnalysisResult result, HtmlDocument doc)
        {
            var scripts = doc.DocumentNode.SelectNodes("//script");
            if (scripts == null) return;

            foreach (var script in scripts)
            {
                var content = script.InnerHtml ?? "";

                if (content.Contains("eval(", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("atob(", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("unescape(", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("document.write(", StringComparison.OrdinalIgnoreCase))
                {
                    result.ObfuscatedJS = true;
                }
            }
        }

        // ------------------------------------------------------------------
        // Hidden IFrames
        // ------------------------------------------------------------------
        private void DetectHiddenIframes(HtmlAnalysisResult result, HtmlDocument doc)
        {
            var iframes = doc.DocumentNode.SelectNodes("//iframe");
            if (iframes == null) return;

            foreach (var frame in iframes)
            {
                var style = frame.GetAttributeValue("style", "").ToLower();
                var width = frame.GetAttributeValue("width", "");
                var height = frame.GetAttributeValue("height", "");

                bool isHidden =
                    style.Contains("display:none") ||
                    style.Contains("visibility:hidden") ||
                    width == "0" ||
                    height == "0";

                if (isHidden)
                {
                    result.HiddenIframe = true;
                }
            }
        }

        // ------------------------------------------------------------------
        // Extract images
        // ------------------------------------------------------------------
        private void ExtractImages(HtmlAnalysisResult result, HtmlDocument doc)
        {
            var imgs = doc.DocumentNode.SelectNodes("//img");
            if (imgs == null)
                return;

            foreach (var img in imgs)
            {
                var src = img.GetAttributeValue("src", "");
                if (!string.IsNullOrWhiteSpace(src))
                    result.Images.Add(src);
            }
        }

        // ------------------------------------------------------------------
        // Contains OAuth Redirect
        // ------------------------------------------------------------------
        private void DetectUnexpectedOAuth(HtmlAnalysisResult result, string redirectHost, string baseDomain)
        {
            if (redirectHost == null || baseDomain == null)
                return;

            try
            {
                    string[] oauthProviders = {
                "accounts.google.com",
                "facebook.com",
                "login.microsoftonline.com",
                "appleid.apple.com",
                "auth0.com"
                };

                bool isOAuth =
                    oauthProviders.Any(provider =>
                        redirectHost.Equals(provider, StringComparison.OrdinalIgnoreCase) ||
                        redirectHost.EndsWith("." + provider, StringComparison.OrdinalIgnoreCase));

                bool isBaseDomainTrusted =
                    baseDomain.Contains("google") ||
                    baseDomain.Contains("youtube") ||
                    baseDomain.Contains("gmail");

                if (isOAuth && !isBaseDomainTrusted)
                {
                    result.OAuthRedirect = true;
                }
            }
            catch
            {
                // ignore
            }
        }

        // ------------------------------------------------------------------
        // Score phishing risk
        // ------------------------------------------------------------------
        private void ComputeRiskScore(HtmlAnalysisResult result, string baseDomain)
        {
            int score = 0;

            // 1. Title references brand (mild)
            if (!string.IsNullOrWhiteSpace(result.Title) &&
                !string.IsNullOrWhiteSpace(baseDomain) &&
                result.Title.Contains(baseDomain, StringComparison.OrdinalIgnoreCase))
            {
                score += 35;
                result.Reasons = result.Reasons + "[Impersonating Title]";
            }

            // 2. Redirect logic
            if (!string.IsNullOrWhiteSpace(result.RedirectLocation))
            {
                var redirectUri = new Uri(result.RedirectLocation);

                if (!redirectUri.Host.EndsWith(baseDomain, StringComparison.OrdinalIgnoreCase))
                {
                    score += 35; // external
                    result.Reasons = result.Reasons + "[External Redirect]";
                }
                else
                {
                    score += 20; // internal
                    result.Reasons = result.Reasons + "[Internal Redirect]";
                }

            }

            // 3. Credential form (strongest)
            if (result.HasCredentialForm)
            {
                score += 60;
                result.Reasons = result.Reasons + "[Has Credential Form]";
            }

            // 4. POST to third-party (very strong)
            if (result.FormPostsToThirdParty)
            {
                score += 40;
                result.Reasons = result.Reasons + "[Third Party Post]";
            }


            // 5. Logo usage (probably wont get hit much)
            if (result.Images.Any(i => i.Contains(baseDomain, StringComparison.OrdinalIgnoreCase)))
            {
                score += 35;
                result.Reasons = result.Reasons + "[Contains Impersonating Logo]";
            }

            //6. Obuscated JS
            if (result.ObfuscatedJS)
            {
                score += 20;
                result.Reasons = result.Reasons + "[Obfuscated JS]";
            }

            //7. Hidden IFrames
            if (result.HiddenIframe)
            {
                result.Reasons += "[Hidden Iframe] ";
                score += 35;
            }

            //8. OAuth Redirect
            if (result.OAuthRedirect)
            {
                result.Reasons += "[Unexpected OAuth Redirect] ";
                score += 60;
            }


            result.RiskScore = score;
        }
    }
}