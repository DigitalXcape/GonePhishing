using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace GonePhishing.Models
{
    public class ScanDbContext : DbContext
    {
        public ScanDbContext(DbContextOptions<ScanDbContext> options) : base(options) { }

        public DbSet<ScanJob> ScanJobs { get; set; }
        public DbSet<DomainTask> DomainTasks { get; set; }
    }

    public class ScanJob
    {
        public int Id { get; set; }
        public string Owner { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string SeedDomains { get; set; }


    }

    public class DomainTask
    {
        public int Id { get; set; }
        public int ScanJobId { get; set; }
        public ScanJob ScanJob { get; set; }

        public string CandidateDomain { get; set; }
        public string IPAddresses { get; set; } //comma separated
        public int? HttpStatus { get; set; }
        public string HttpReason { get; set; }
        public string Error { get; set; }

        public string BaseDomain { get; set; }

        public DomainState State { get; set; } = DomainState.Pending;
        public DateTime? ProcessedAt { get; set; }
    }

    public enum DomainState
    {
        Pending,
        Processing,
        Done,
        Error
    }
}
