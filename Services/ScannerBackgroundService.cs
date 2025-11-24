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
        private readonly HtmlService _htmlService;

        // Constructor
        public ScannerBackgroundService(IServiceProvider svcProvider, ILogger<ScannerBackgroundService> logger, HtmlService htmlService)
        {
            _svcProvider = svcProvider;
            _logger = logger;
            _htmlService = htmlService;

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

        // Handles DNS resolution, ownership checks, HTTP fetch, and HTML analysis
        private async Task ProcessDomainTask(DomainTask task, ScanDbContext db, CancellationToken ct)
        {
            try
            {
                task.LookUpStatus = LookUpStatus.Unknown;

                // --------------------------------------------------------------
                // DNS: Resolve candidate domain
                // --------------------------------------------------------------
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

                // No DNS = not reachable
                if (!candidateIps.Any())
                {
                    task.State = DomainState.Done;
                    task.Error = "Excluded: no IPs";
                    task.LookUpStatus = LookUpStatus.NoIP;
                    task.ProcessedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    return;
                }

                // --------------------------------------------------------------
                // OWNER CHECK — compare candidate ASN vs base domain ASN
                // --------------------------------------------------------------
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
                        // Collect ASN "signature" for base IPs
                        var baseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var ip in baseIps)
                        {
                            var w = await WhoIsService.GetWhoIsInfoAsync(ip);
                            if (w == null) continue;

                            if (!string.IsNullOrWhiteSpace(w.asn)) baseSet.Add(w.asn);
                            if (!string.IsNullOrWhiteSpace(w.as_domain)) baseSet.Add(w.as_domain);
                            if (!string.IsNullOrWhiteSpace(w.as_name)) baseSet.Add(w.as_name);
                        }

                        // Compare candidate IPs against that signature
                        foreach (var cip in candidateIps)
                        {
                            var w = await WhoIsService.GetWhoIsInfoAsync(cip);
                            if (w == null) continue;

                            if ((!string.IsNullOrWhiteSpace(w.asn) && baseSet.Contains(w.asn)) ||
                                (!string.IsNullOrWhiteSpace(w.as_domain) && baseSet.Contains(w.as_domain)) ||
                                (!string.IsNullOrWhiteSpace(w.as_name) && baseSet.Contains(w.as_name)))
                            {
                                task.State = DomainState.Done;
                                task.Error = "Excluded: owned by base ASN";
                                task.LookUpStatus = LookUpStatus.OwnedByOrigin;
                                task.ProcessedAt = DateTime.UtcNow;
                                await db.SaveChangesAsync(ct);
                                return;
                            }
                        }
                    }
                }

                // --------------------------------------------------------------
                // HTTP Request — Try HTTP and HTTPS
                // --------------------------------------------------------------
                HttpResponseMessage resp = null;
                string html = null;

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

                        if (resp != null)
                        {
                            task.HttpStatus = (int?)resp.StatusCode;
                            task.HttpReason = resp.ReasonPhrase ?? "";

                            html = await resp.Content.ReadAsStringAsync(ct);
                            break;
                        }
                    }
                    catch { }
                }

                // If still no response → nothing to analyze
                if (resp == null)
                {
                    task.State = DomainState.Done;
                    task.Error = "No HTTP response";
                    task.LookUpStatus = LookUpStatus.NoIP;
                    task.ProcessedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    return;
                }

                // --------------------------------------------------------------
                // HTML Analysis using HtmlService (only if allowed)
                // --------------------------------------------------------------
                if (task.LookUpStatus == LookUpStatus.Unknown && !string.IsNullOrWhiteSpace(html))
                {
                    var htmlService = new HtmlService();
                    var analysis = await htmlService.AnalyzeAsync(resp.RequestMessage.RequestUri.ToString(), task.BaseDomain);

                    task.HtmlScore = analysis.RiskScore;

                    if (analysis.RiskScore >= 100)
                    {
                        task.LookUpStatus = LookUpStatus.Danger;

                        task.RiskReasons = analysis.Reasons;

                        // --------------------------
                        // Create report entry
                        // --------------------------
                        var report = new ScanJobReportItem
                        {
                            TypoDomain = task.CandidateDomain,
                            Reasons = analysis.Reasons ?? "High HTML risk score",
                            ScannedAt = DateTime.UtcNow,
                            ScanJob = await db.ScanJobs.FindAsync(task.ScanJobId)   // link job
                        };

                        db.ScanJobReports.Add(report);
                    }
                    else if (analysis.RiskScore >= 30)
                    {
                        task.LookUpStatus = LookUpStatus.Suspicious;

                        task.RiskReasons = analysis.Reasons;
                    }
                    else
                    {
                        task.LookUpStatus = LookUpStatus.Safe;
                    }

                }

                // --------------------------------------------------------------
                // Finalize
                // --------------------------------------------------------------
                task.State = DomainState.Done;
                task.ProcessedAt = DateTime.UtcNow;
                task.Error = task.Error ?? "";

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                task.State = DomainState.Error;
                task.Error = ex.Message ?? "Unknown error";
                task.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
    }
}