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

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _svcProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ScanDbContext>();

                    // Get the next pending task
                    var next = await db.DomainTasks
                        .Where(t => t.State == DomainState.Pending)
                        .OrderBy(t => t.Id)
                        .FirstOrDefaultAsync(stoppingToken);

                    if (next == null)
                    {
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    // Mark as processing
                    next.State = DomainState.Processing;
                    await db.SaveChangesAsync(stoppingToken);

                    // Process DNS and HTTP
                    await ProcessDomainTask(next, db, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break; // shutting down
                }
                catch (Exception ex)
                {
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
                    var addresses = await Dns.GetHostAddressesAsync(task.CandidateDomain);

                    foreach (var a in addresses)
                        ips.Add(a.ToString());

                    task.IPAddresses = string.Join(",", ips) ?? "";
                }
                catch (Exception dnsEx)
                {
                    task.IPAddresses = "";
                    _logger.LogInformation("DNS failed for {d}: {e}", task.CandidateDomain, dnsEx.Message);
                }

                // ---------------------------
                // HTTP Check (try both HTTP & HTTPS)
                // ---------------------------
                try
                {
                    HttpResponseMessage resp = null;
                    var urls = new[] { $"http://{task.CandidateDomain}/", $"https://{task.CandidateDomain}/" };

                    foreach (var u in urls)
                    {
                        try
                        {
                            var request = new HttpRequestMessage(HttpMethod.Get, u);
                            resp = await _http.SendAsync(request, ct);
                            if (resp != null) break;
                        }
                        catch (HttpRequestException) { }
                        catch (TaskCanceledException) { }
                    }

                    task.HttpStatus = resp != null ? (int?)resp.StatusCode : null;
                    task.HttpReason = resp?.ReasonPhrase ?? "";
                }
                catch (Exception httpEx)
                {
                    task.HttpStatus = null;
                    task.HttpReason = "";
                    _logger.LogInformation("HTTP check error for {d}: {e}", task.CandidateDomain, httpEx.Message);
                }

                // ---------------------------
                // Finalize task
                // ---------------------------
                task.State = DomainState.Done;
                task.ProcessedAt = DateTime.UtcNow;

                // Ensure Error is not null
                task.Error = task.Error ?? "";

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // Mark as error
                task.State = DomainState.Error;
                task.Error = ex.Message ?? "Unknown error";
                task.ProcessedAt = DateTime.UtcNow;

                // Safe defaults
                task.IPAddresses = task.IPAddresses ?? "";
                task.HttpReason = task.HttpReason ?? "";
                task.HttpStatus ??= null;

                await db.SaveChangesAsync(ct);
            }
        }
    }
}