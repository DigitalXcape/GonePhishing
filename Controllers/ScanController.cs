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

        // -------------------------------
        // Handle submission of domain list
        // -------------------------------
        [HttpPost]
        public async Task<IActionResult> Start(string domains)
        {
            // If input is empty, redirect back to the input form
            if (string.IsNullOrWhiteSpace(domains))
                return RedirectToAction(nameof(Index));

            // Create a new ScanJob entry
            var job = new ScanJob
            {
                CreatedAt = DateTime.UtcNow,
                SeedDomains = domains,
                Owner = "Anonymous"
            };

            // Save the ScanJob to DB
            _db.ScanJobs.Add(job);
            await _db.SaveChangesAsync();

            // Split the input into lines, trim whitespace, remove empty lines, remove duplicates
            var lines = domains.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct();

            // For each seed domain, generate typo-squatted variants
            foreach (var seed in lines)
            {
                foreach (var v in TypoGenerator.GenerateVariants(seed))
                {
                    _db.DomainTasks.Add(new DomainTask
                    {
                        ScanJobId = job.Id,
                        CandidateDomain = v,
                        State = DomainState.Pending,
                        IPAddresses = "",
                        HttpReason = "",
                        Error = ""
                    });
                }
            }

            // Save all tasks to DB
            await _db.SaveChangesAsync();

            // Redirect to the Status page to see progress
            return RedirectToAction(nameof(Status), new { id = job.Id });
        }

        // -------------------------------
        // Display the status page for a job
        // -------------------------------
        [HttpGet]
        public async Task<IActionResult> Status(int id)
        {
            // Load the job from DB
            var job = await _db.ScanJobs.FindAsync(id);
            if (job == null) return NotFound();

            // Load all tasks associated with this job
            var tasks = await _db.DomainTasks
                .Where(t => t.ScanJobId == id)
                .OrderBy(t => t.Id)
                .ToListAsync();

            // Pass job info to the view
            ViewData["Job"] = job;

            // Return the view with the list of tasks
            return View(tasks);
        }

        // -------------------------------
        // Return job status as JSON (for AJAX polling)
        // -------------------------------
        [HttpGet]
        public async Task<JsonResult> StatusJson(int id)
        {
            // Load tasks for the job and select only necessary fields
            var tasks = await _db.DomainTasks
                .Where(t => t.ScanJobId == id)
                .OrderBy(t => t.Id)
                .Select(t => new {
                    t.Id,
                    t.CandidateDomain,
                    t.State,
                    t.IPAddresses,
                    t.HttpStatus,
                    t.HttpReason,
                    t.Error,
                    t.ProcessedAt
                }).ToListAsync();

            // Return JSON object with task array
            return Json(new { tasks });
        }
    }
}
