using GonePhishing.Models;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;

namespace GonePhishing.Services
{
    public class ReportingResult
    {
        /// <summary>
        /// True if the report was successfully submitted to Cloudflare.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Optional message returned by Cloudflare or internal logic.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Optional HTTP status code indicating server response.
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// Optional payload for debugging or developer inspection.
        /// </summary>
        public string? RawResponse { get; set; }

        public ReportingResult(bool success, string message = "", int statusCode = 0, string? raw = null)
        {
            Success = success;
            Message = message;
            StatusCode = statusCode;
            RawResponse = raw;
        }
    }

    public class ReportingService
    {
        private static readonly HttpClient client = new HttpClient();

        public static async Task<ReportingResult?> ReportDomainsAsync(List<string> urls, TimeSpan? timeout = null)
        {
            if (urls == null || urls.Count == 0)
                return new ReportingResult(false, "No URLs provided.");

            try
            {
                // Cloudflare API settings
                string accountId = "201014e8334e82c024a73ea6547317fb";
                string apiToken = "lMsiwgoKz48wjcVIDthTaT6V_AioAiQ_zqJmTVIU";

                if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(apiToken))
                    return new ReportingResult(false, "Cloudflare credentials not configured.");

                string endpoint =
                    $"https://api.cloudflare.com/client/v4/accounts/{accountId}/abuse-reports/phishing";

                if (timeout.HasValue)
                    client.Timeout = timeout.Value;

                // Convert URL list → newline separated, Cloudflare required format
                string urlBlob = string.Join("\n", urls);

                // Build request payload (required phishing fields)
                var payload = new
                {
                    act = "abuse_phishing",
                    email = "abel9043@mstc.edu",
                    email2 = "abel9043@mstc.edu",
                    host_notification = "send",
                    owner_notification = "send",
                    justification = "Automated phishing report.",
                    name = "Gone Phishing",
                    urls = urlBlob
                };

                string jsonBody = JsonSerializer.Serialize(payload);

                var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
                req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                HttpResponseMessage res = await client.SendAsync(req);

                string raw = await res.Content.ReadAsStringAsync();

                if (!res.IsSuccessStatusCode)
                {
                    return new ReportingResult(false,
                        $"Cloudflare returned error {res.StatusCode}",
                        (int)res.StatusCode,
                        raw);
                }


                return new ReportingResult(true, "Phishing report submitted.", (int)res.StatusCode, raw);
            }
            catch (Exception ex)
            {
                return new ReportingResult(false, ex.Message, 0, ex.ToString());
            }
        }
    }
}
