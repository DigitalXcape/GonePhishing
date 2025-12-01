using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace GonePhishing.Models
{
    public class ScanDbContext : DbContext
    {
        public ScanDbContext(DbContextOptions<ScanDbContext> options) : base(options) { }

        public DbSet<ScanJob> ScanJobs { get; set; }
        public DbSet<DomainTask> DomainTasks { get; set; }

        public DbSet<ScanJobReportItem> ScanJobReports { get; set; }

        public DbSet<ReportedDomain> DomainsReported { get; set; }
    }

    public class ScanJob
    {
        public int Id { get; set; }
        public string Owner { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string SeedDomains { get; set; }

        public int NumberOfTypoDomains { get; set; }
    }

    public class DomainTask
    {
        public int Id { get; set; }
        public int ScanJobId { get; set; }
        public ScanJob ScanJob { get; set; }

        // Domain + Networking
        public string CandidateDomain { get; set; }
        public string BaseDomain { get; set; }
        public string? IPAddresses { get; set; }

        // HTTP Response
        public int? HttpStatus { get; set; }
        public string? HttpReason { get; set; }

        // HTML Analysis Result
        public int? HtmlScore { get; set; }
        public string? HtmlTitle { get; set; }
        public string? HtmlTextPreview { get; set; }
        public bool? ContainsSuspiciousForms { get; set; }
        public bool? ContainsBrandKeywords { get; set; }
        public bool? HasObfuscatedScripts { get; set; }

        public string? RedirectLocation { get; set; }

        // Final Scoring
        public int? TotalRiskScore { get; set; }
        public RiskLevel? RiskLevel { get; set; }

        public string? RiskReasons {  get; set; }

        // Status
        public LookUpStatus LookUpStatus { get; set; }
        public DomainState State { get; set; } = DomainState.Pending;
        public string? Error { get; set; }

        public DateTime? ProcessedAt { get; set; }
    }

    public class ScanJobReportItem
    {
        public int Id { get; set; }
        public string TypoDomain { get; set; }
        public string Reasons { get; set; }
        public DateTime? ScannedAt { get; set; }

        public ScanJob ScanJob { get; set; }

        public int ScanJobId { get; set; }
    }

    public class ReportedDomain
    {
        public int Id { get; set; }
        public string TypoDomain { get; set; }
        public DateTime? TimeReported {  get; set; }
    }

    public enum DomainState
    {
        Pending,
        Processing,
        Done,
        Error
    }

    public enum LookUpStatus
    {
        Unknown,
        NoIP,
        OwnedByOrigin,
        HtmlChecked,
        Safe,
        Suspicious,
        Danger,
        Error
    }

    public enum RiskLevel
    {
        Safe,
        Suspicious,
        Dangerous
    }
}
