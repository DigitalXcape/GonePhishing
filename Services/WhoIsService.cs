using GonePhishing.Models;
using System.Text.Json;

namespace GonePhishing.Services
{
    public static class WhoIsService
    {
        private static readonly string token = "7da7760ba588fc";

        // Public async method returns WhoIsInfo or null on error 
        public static async Task<WhoIsInfo?> GetWhoIsInfoAsync(string ipAddress, TimeSpan? timeout = null)
        {
            try
            {
                using var http = new HttpClient
                {
                    Timeout = timeout ?? TimeSpan.FromSeconds(5)
                };

                var url = $"https://api.ipinfo.io/lite/{ipAddress}?token={token}";

                var response = await http.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStringAsync();

                var info = JsonSerializer.Deserialize<WhoIsInfo>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return info;
            }
            catch
            {
                // swallow - return null if whois fails
                return null;
            }
        }
    }
}
