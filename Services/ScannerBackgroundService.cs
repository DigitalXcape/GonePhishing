using GonePhishing.Models;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace GonePhishing.Services
{
    // BackgroundService runs continuously in the background while the ASP.NET app is running
    public class ScannerBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _svcProvider; // for creating scoped services (like DbContext)
        private readonly ILogger<ScannerBackgroundService> _logger; // logging progress and errors
        private readonly HttpClient _http; // reusable HttpClient for HTTP requests

        // Constructor
        public ScannerBackgroundService(IServiceProvider svcProvider, ILogger<ScannerBackgroundService> logger)
        {
            _svcProvider = svcProvider;
            _logger = logger;

            // Initialize HttpClient with a small timeout to prevent long waits
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("LabScanner/1.0");
        }

        // Main loop of the background service
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ScannerBackgroundService started.");

            // Loop continuously until service is stopped
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Create a new scope to get a fresh DbContext instance for this iteration
                    using var scope = _svcProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ScanDbContext>();

                    // Pick the next domain task that is still pending
                    var next = await db.DomainTasks
                        .Where(t => t.State == DomainState.Pending)
                        .OrderBy(t => t.Id)
                        .FirstOrDefaultAsync(stoppingToken);

                    if (next == null)
                    {
                        // No pending tasks, wait 1 second before checking again
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    // Mark the task as currently being processed
                    next.State = DomainState.Processing;
                    await db.SaveChangesAsync(stoppingToken);

                    // Perform DNS + HTTP checks
                    await ProcessDomainTask(next, db, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // Service is shutting down, break the loop
                    break;
                }
                catch (Exception ex)
                {
                    // Log any unexpected exceptions, wait a moment before retrying
                    _logger.LogError(ex, "Background loop error.");
                    await Task.Delay(2000, stoppingToken);
                }
            }

            _logger.LogInformation("ScannerBackgroundService stopping.");
        }

        // Handles DNS resolution and HTTP requests for a single DomainTask
        private async Task ProcessDomainTask(DomainTask task, ScanDbContext db, CancellationToken ct)
        {
            try
            {
                // ---------------------------
                // DNS Resolution
                // ---------------------------
                try
                {
                    var ips = new List<string>();

                    // Get all IP addresses for the domain
                    var addresses = await Dns.GetHostAddressesAsync(task.CandidateDomain);
                    foreach (var a in addresses) ips.Add(a.ToString());

                    task.IPAddresses = string.Join(",", ips);
                }
                catch (Exception dnsEx)
                {
                    // If DNS fails, store empty IPs but continue to HTTP check
                    task.IPAddresses = "";
                    _logger.LogInformation("DNS failed for {d}: {e}", task.CandidateDomain, dnsEx.Message);
                }

                // ---------------------------
                // HTTP Check (try both HTTP & HTTPS)
                // ---------------------------
                try
                {
                    HttpResponseMessage resp = null;
                    var tried = new[] { $"http://{task.CandidateDomain}/", $"https://{task.CandidateDomain}/" };

                    // Try each URL until one succeeds
                    foreach (var u in tried)
                    {
                        try
                        {
                            var request = new HttpRequestMessage(HttpMethod.Get, u);
                            resp = await _http.SendAsync(request, ct);
                            if (resp != null) break; // success, stop trying
                        }
                        catch (HttpRequestException)
                        {
                            // network errors, continue to next URL
                        }
                        catch (TaskCanceledException)
                        {
                            // timeout, continue to next URL
                        }
                    }

                    // If we got a response, store status code & reason
                    if (resp != null)
                    {
                        task.HttpStatus = (int)resp.StatusCode;
                        task.HttpReason = resp.ReasonPhrase;
                    }
                }
                catch (Exception httpEx)
                {
                    // Catch unexpected HTTP errors
                    _logger.LogInformation("HTTP check error for {d}: {e}", task.CandidateDomain, httpEx.Message);
                }

                // ---------------------------
                // Mark task as completed
                // ---------------------------
                task.State = DomainState.Done;
                task.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // If something unexpected happens, mark task as Error
                task.State = DomainState.Error;
                task.Error = ex.Message;
                task.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
    }
}