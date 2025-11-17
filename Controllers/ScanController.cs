using GonePhishing.Models;
using GonePhishing.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GonePhishing.Controllers
{
    // Controller that handles scanning domain lists and tracking their status
    public class ScanController : Controller
    {
        private readonly ScanDbContext _db; // Database context for storing jobs and tasks

        // Constructor: inject DbContext
        public ScanController(ScanDbContext db)
        {
            _db = db;
        }

        // -------------------------------
        // Display the initial webform
        // -------------------------------
        [HttpGet]
        public IActionResult Index() => View(); // Returns Index.cshtml where user can input domains

        //start the scan
        [HttpPost]
        public async Task<IActionResult> Start(string domains, IFormFile domainFile, bool ignoreDuplicates)
        {
            // -----------------------------------------
            // Collect all domains from textarea + file
            // -----------------------------------------
            var allDomains = new List<string>();

            if (!string.IsNullOrWhiteSpace(domains))
            {
                var lines = domains.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(l => l.Trim())
                                   .Where(l => !string.IsNullOrWhiteSpace(l));

                allDomains.AddRange(lines);
            }

            if (domainFile != null && domainFile.Length > 0)
            {
                using var reader = new StreamReader(domainFile.OpenReadStream());
                while (!reader.EndOfStream)
                {
                    var line = (await reader.ReadLineAsync())?.Trim();
                    if (!string.IsNullOrEmpty(line))
                        allDomains.Add(line);
                }
            }

            if (!allDomains.Any())
                return RedirectToAction(nameof(Index));

            // Normalize + dedupe initial list
            allDomains = allDomains
                .Select(d => d.Trim().ToLower())
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct()
                .ToList();


            // ----------------------------------------------------
            // DUPLICATES WITHIN INPUT (if ignoreDuplicates == true)
            // ----------------------------------------------------
            if (ignoreDuplicates)
            {
                // Look for existing jobs with same base domains
                foreach (var domain in allDomains)
                {
                    var oldJobs = _db.ScanJobs
                                     .Where(j => j.SeedDomains.Contains(domain))
                                     .ToList();

                    foreach (var j in oldJobs)
                    {
                        // Delete tasks
                        var tasks = _db.DomainTasks.Where(t => t.ScanJobId == j.Id);
                        _db.DomainTasks.RemoveRange(tasks);

                        // Delete job
                        _db.ScanJobs.Remove(j);
                    }
                }

                await _db.SaveChangesAsync();
            }


            // -----------------------------------------------------------------
            // REMOVE OLD (6+ months) JOBS AND RESCAN; SKIP RECENT ONES (< 6 mo)
            // -----------------------------------------------------------------
            var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);

            var domainsToScan = new List<string>();

            foreach (var baseDomain in allDomains)
            {
                var matchingJobs = _db.ScanJobs
                                      .Where(j => j.SeedDomains.Contains(baseDomain))
                                      .OrderByDescending(j => j.CreatedAt)
                                      .ToList();

                if (!matchingJobs.Any())
                {
                    // No previous record → must scan
                    domainsToScan.Add(baseDomain);
                    continue;
                }

                var latestJob = matchingJobs.First();

                if (latestJob.CreatedAt < sixMonthsAgo)
                {
                    // Old record → delete & rescan
                    var oldTasks = _db.DomainTasks.Where(t => t.ScanJobId == latestJob.Id);
                    _db.DomainTasks.RemoveRange(oldTasks);
                    _db.ScanJobs.Remove(latestJob);
                    await _db.SaveChangesAsync();

                    domainsToScan.Add(baseDomain); // rescan
                }
                else
                {
                    // Recent → skip scanning
                    continue;
                }
            }

            // If nothing to scan, redirect to last status page
            if (!domainsToScan.Any())
            {
                TempData["Message"] = "All domains already scanned within the last 6 months.";
                return RedirectToAction(nameof(Index));
            }


            // -----------------------------------------
            // Create new ScanJob for *this batch*
            // -----------------------------------------
            var job = new ScanJob
            {
                CreatedAt = DateTime.UtcNow,
                SeedDomains = string.Join("\n", domainsToScan),
                Owner = "Anonymous",
                NumberOfTypoDomains = 0
            };

            _db.ScanJobs.Add(job);
            await _db.SaveChangesAsync();


            // ----------------------------------------------------
            // Generate typo tasks for domainsToScan
            // ----------------------------------------------------
            foreach (var seed in domainsToScan)
            {
                foreach (var variant in TypoGenerator.GenerateVariants(seed))
                {
                    _db.DomainTasks.Add(new DomainTask
                    {
                        ScanJobId = job.Id,
                        CandidateDomain = variant,
                        State = DomainState.Pending,
                        IPAddresses = "",
                        HttpReason = "",
                        Error = "",
                        BaseDomain = seed
                    });

                    job.NumberOfTypoDomains++;
                }
            }

            await _db.SaveChangesAsync();


            return RedirectToAction(nameof(Status), new { id = job.Id });
        }

        //Displays the status page of a job
        [HttpGet]
        public async Task<IActionResult> Status(int id, int page = 1, int pageSize = 200)
        {
            var job = await _db.ScanJobs.FindAsync(id);
            if (job == null) return NotFound();

            // Count total rows
            int total = await _db.DomainTasks
                .Where(t => t.ScanJobId == id)
                .CountAsync();

            // Fetch only the slice for this page
            var tasks = await _db.DomainTasks
                .Where(t => t.ScanJobId == id)
                .OrderBy(t => t.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Send values to view
            ViewData["Job"] = job;
            ViewData["Total"] = total;
            ViewData["Page"] = page;
            ViewData["PageSize"] = pageSize;

            return View(tasks);
        }

        // -------------------------------
        // Return job status as JSON (for AJAX polling)
        // -------------------------------
        [HttpGet]
        public async Task<JsonResult> StatusJson(int id, int page = 1, int pageSize = 200)
        {
            var tasks = await _db.DomainTasks
                .Where(t => t.ScanJobId == id)
                .OrderBy(t => t.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new {
                    t.Id,
                    t.CandidateDomain,
                    t.State,
                    t.HtmlScore,
                    t.IPAddresses,
                    t.HttpStatus,
                    t.HttpReason,
                    t.Error,
                    t.ProcessedAt,
                    LookUpStatus = (int)t.LookUpStatus
                })
                .ToListAsync();

            return Json(new { tasks });
        }
    }
}
