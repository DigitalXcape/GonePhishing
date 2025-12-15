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
            var result = new HtmlAnalysisResult { Url = url };

            HttpResponseMessage response;
            try
            {
                response = await _client.GetAsync(url);
            }
            catch
            {
                return result; // unreachable or timeout
            }

            // Load HTML body
            result.Html = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(result.Html))
                return result;

            // Parse DOM
            var doc = new HtmlDocument();
            doc.LoadHtml(result.Html);

            // Extract features
            ExtractTitle(result, doc);
            ExtractForms(result, doc, baseDomain);
            ExtractImages(result, doc);
            DetectHiddenIframes(result, doc);
            DetectSuspiciousJavaScript(result, doc);
            DetectFormOAuthRedirect(result, doc, baseDomain);
            DetectJsRedirect(result, doc);
            DetectMetaRefreshRedirect(result, doc, baseDomain);

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
        // Detect forms that submit to OAuth providers
        // ------------------------------------------------------------------
        private void DetectFormOAuthRedirect(HtmlAnalysisResult result, HtmlDocument doc, string baseDomain)
        {
            var forms = doc.DocumentNode.SelectNodes("//form");
            if (forms == null || baseDomain == null) return;

            string[] oauthProviders = {
        "accounts.google.com",
        "facebook.com",
        "login.microsoftonline.com",
        "appleid.apple.com",
        "auth0.com"
    };

            foreach (var form in forms)
            {
                var action = form.GetAttributeValue("action", "")?.Trim();
                if (string.IsNullOrWhiteSpace(action)) continue;

                if (!Uri.TryCreate(action, UriKind.RelativeOrAbsolute, out var uri)) continue;
                string host = uri.IsAbsoluteUri ? uri.Host : new Uri(new Uri(result.Url), uri).Host;

                bool isOAuth = oauthProviders.Any(provider =>
                    host.Equals(provider, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + provider, StringComparison.OrdinalIgnoreCase));

                bool isBaseDomainTrusted = baseDomain.Contains("google") || baseDomain.Contains("youtube") || baseDomain.Contains("gmail");

                if (isOAuth && !isBaseDomainTrusted)
                {
                    result.OAuthRedirect = true;
                    break;
                }
            }
        }

        // ------------------------------------------------------------------
        // Detect JavaScript redirects
        // ------------------------------------------------------------------
        private void DetectJsRedirect(HtmlAnalysisResult result, HtmlDocument doc)
        {
            var scripts = doc.DocumentNode.SelectNodes("//script");
            if (scripts == null) return;

            foreach (var script in scripts)
            {
                var content = script.InnerHtml ?? "";

                if (content.Contains("window.location", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("location.href", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("location.replace", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("document.location", StringComparison.OrdinalIgnoreCase))
                {
                    result.RedirectLocation = "JavaScript redirect detected";
                    break;
                }
            }
        }

        // ------------------------------------------------------------------
        // Detect meta refresh redirects
        // ------------------------------------------------------------------
        private void DetectMetaRefreshRedirect(HtmlAnalysisResult result, HtmlDocument doc, string baseDomain)
        {
            var metas = doc.DocumentNode.SelectNodes("//meta[@http-equiv]");
            if (metas == null) return;

            foreach (var meta in metas)
            {
                var httpEquiv = meta.GetAttributeValue("http-equiv", "").ToLower();
                if (httpEquiv != "refresh") continue;

                var content = meta.GetAttributeValue("content", "");
                if (string.IsNullOrWhiteSpace(content)) continue;

                // Usually content="5; url=https://example.com"
                var parts = content.Split(';', 2);
                if (parts.Length == 2)
                {
                    var urlPart = parts[1].Trim();
                    if (urlPart.StartsWith("url=", StringComparison.OrdinalIgnoreCase))
                    {
                        var redirectUrl = urlPart.Substring(4).Trim('\'', '"');
                        result.RedirectLocation = redirectUrl;
                        break;
                    }
                }
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
                result.Reasons += "[Hidden Iframe]";
                score += 50;
            }

            //8. OAuth Redirect
            if (result.OAuthRedirect)
            {
                result.Reasons += "[OAuth Redirect]";
                score += 50;
            }

            result.RiskScore = score;
        }
    }
}