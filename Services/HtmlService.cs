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
        public List<string> Images { get; set; } = new();
        public string RedirectLocation { get; set; }

        public int RiskScore { get; set; }
    }

    public class HtmlService
    {
        private readonly HttpClient _client;

        public HtmlService()
        {
            _client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false // we want to detect redirects
            })
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        // ------------------------------------------------------------------
        // Fetch HTML (detect redirect manually)
        // ------------------------------------------------------------------
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

            // Detect HTTP redirect (30x)
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
            {
                result.RedirectLocation = response.Headers.Location?.ToString();
            }

            // Load HTML body
            result.Html = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(result.Html))
                return result;

            // Parse DOM
            var doc = new HtmlDocument();
            doc.LoadHtml(result.Html);

            ExtractTitle(result, doc);
            ExtractForms(result, doc, baseDomain);
            ExtractImages(result, doc);

            // Compute score
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
        // Score phishing risk
        // ------------------------------------------------------------------
        private void ComputeRiskScore(HtmlAnalysisResult result, string baseDomain)
        {
            int score = 0;

            // --------------------------------------------------------------
            // 1. Title contains the brand/base domain → mild impersonation
            // --------------------------------------------------------------
            if (!string.IsNullOrWhiteSpace(result.Title) &&
                !string.IsNullOrWhiteSpace(baseDomain) &&
                result.Title.Contains(baseDomain, StringComparison.OrdinalIgnoreCase))
            {
                score += 10; // Mild risk: page title references target brand
            }

            // --------------------------------------------------------------
            // 2. Redirects (internal vs external)
            // --------------------------------------------------------------
            if (!string.IsNullOrWhiteSpace(result.RedirectLocation))
            {
                var redirectUri = new Uri(result.RedirectLocation);

                if (!redirectUri.Host.EndsWith(baseDomain, StringComparison.OrdinalIgnoreCase))
                {
                    score += 20; // High risk: redirect points outside the base domain
                }
                else
                {
                    score += 5;  // Low risk: redirect remains within same domain
                }
            }

            // --------------------------------------------------------------
            // 3. Credential form detected
            // --------------------------------------------------------------
            if (result.HasCredentialForm)
                score += 70; // Very high risk: page contains username/password form

            // --------------------------------------------------------------
            // 4. Form POSTs to a different domain
            // --------------------------------------------------------------
            if (result.FormPostsToThirdParty)
                score += 20; // High risk: credentials would be sent to a third party

            // --------------------------------------------------------------
            // 5. Any redirect present (secondary signal)
            // --------------------------------------------------------------
            if (!string.IsNullOrWhiteSpace(result.RedirectLocation))
                score += 30; // Additional small risk: page performs redirect behavior

            // --------------------------------------------------------------
            // 6. Logo file detected
            // --------------------------------------------------------------
            if (result.Images.Any(i => i.Contains("logo", StringComparison.OrdinalIgnoreCase)))
                score += 10; // Mild risk: presence of "logo" suggests branding impersonation

            // --------------------------------------------------------------
            // Final score
            // --------------------------------------------------------------
            result.RiskScore = score;
        }
    }
}