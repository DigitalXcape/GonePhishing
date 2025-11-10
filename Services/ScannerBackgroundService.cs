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
                task.LookUpStatus = LookUpStatus.Unknown;

                // ---------------------------
                // DNS Resolution for candidate
                // ---------------------------
                var candidateIps = new List<string>();
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(task.CandidateDomain);
                    foreach (var a in addresses)
                        candidateIps.Add(a.ToString());

                    task.IPAddresses = string.Join(",", candidateIps) ?? "";
                }
                catch
                {
                    task.IPAddresses = "";
                }

                // If candidate has 0 IPs → no need to continue
                if (!candidateIps.Any())
                {
                    task.State = DomainState.Done;
                    task.Error = "Excluded: no IPs";
                    task.LookUpStatus = LookUpStatus.NoIP;
                    task.ProcessedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    return;
                }

                // ---------------------------
                // OWNER CHECK — compare to base domain
                // ---------------------------
                if (!string.IsNullOrWhiteSpace(task.BaseDomain) &&
                    !string.Equals(task.BaseDomain, task.CandidateDomain, StringComparison.OrdinalIgnoreCase))
                {
                    var baseIps = new List<string>();
                    try
                    {
                        var addrs = await Dns.GetHostAddressesAsync(task.BaseDomain);
                        foreach (var a in addrs) baseIps.Add(a.ToString());
                    }
                    catch { }

                    if (baseIps.Any())
                    {
                        // get ASN signature set for base
                        var baseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var ip in baseIps)
                        {
                            var w = await WhoIsService.GetWhoIsInfoAsync(ip);
                            if (w == null) continue;

                            if (!string.IsNullOrWhiteSpace(w.asn)) baseSet.Add(w.asn);
                            if (!string.IsNullOrWhiteSpace(w.as_domain)) baseSet.Add(w.as_domain);
                            if (!string.IsNullOrWhiteSpace(w.as_name)) baseSet.Add(w.as_name);
                        }

                        // compare candidate
                        foreach (var cip in candidateIps)
                        {
                            var w = await WhoIsService.GetWhoIsInfoAsync(cip);
                            if (w == null) continue;

                            if ((!string.IsNullOrWhiteSpace(w.asn) && baseSet.Contains(w.asn))
                                || (!string.IsNullOrWhiteSpace(w.as_domain) && baseSet.Contains(w.as_domain))
                                || (!string.IsNullOrWhiteSpace(w.as_name) && baseSet.Contains(w.as_name)))
                            {
                                task.State = DomainState.Done;
                                task.Error = "Excluded: owned by base ASN";
                                task.LookUpStatus = LookUpStatus.OwnedByOrgin;
                                task.ProcessedAt = DateTime.UtcNow;
                                await db.SaveChangesAsync(ct);
                                return;
                            }
                        }
                    }
                }

                try
                {
                    HttpResponseMessage resp = null;

                    var urls = new[]
                    {
                        $"http://{task.CandidateDomain}/",
                        $"https://{task.CandidateDomain}/"
                    };

                    foreach (var u in urls)
                    {
                        try
                        {
                            var req = new HttpRequestMessage(HttpMethod.Get, u);
                            resp = await _http.SendAsync(req, ct);
                            if (resp != null) break;
                        }
                        catch { }
                    }

                    task.HttpStatus = resp != null ? (int?)resp.StatusCode : null;
                    task.HttpReason = resp?.ReasonPhrase ?? "";

                    // ---------------------------
                    // Mark as Danger if conditions met
                    // ---------------------------
                    if (candidateIps.Any() && task.HttpStatus != null && task.LookUpStatus == LookUpStatus.Unknown)
                    {
                        task.LookUpStatus = LookUpStatus.Danger;
                    }
                }
                catch
                {
                    task.HttpStatus = null;
                    task.HttpReason = "";
                }

                // ---------------------------
                // Finalize
                // ---------------------------
                task.State = DomainState.Done;
                task.ProcessedAt = DateTime.UtcNow;
                task.Error = task.Error ?? "";

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // fallback
                task.State = DomainState.Error;
                task.Error = ex.Message ?? "Unknown error";
                task.ProcessedAt = DateTime.UtcNow;

                await db.SaveChangesAsync(ct);
            }
        }
    }
}