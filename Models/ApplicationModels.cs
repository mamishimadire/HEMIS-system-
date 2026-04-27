using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HemisAudit.Models
{
    // ═══════════════════════════════════════════════════════════════════════════
    // APPLICATION USER  (extends ASP.NET Identity)
    // ═══════════════════════════════════════════════════════════════════════════
    public class ApplicationUser : IdentityUser
    {
        [Required, MaxLength(100)]
        public string FirstName { get; set; } = "";

        [Required, MaxLength(100)]
        public string LastName { get; set; } = "";

        public string FullName => $"{FirstName} {LastName}".Trim();

        [MaxLength(20)]
        public string EmployeeCode { get; set; } = "";     // e.g. MADM007

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }
        public DateTime? PasswordSetDate { get; set; } = DateTime.UtcNow;
        public DateTime? PasswordChangedAt { get; set; }
        public string? PasswordHistory { get; set; }
        public string? ProfilePicturePath { get; set; }

        [MaxLength(50)]
        public string? Gender { get; set; }

        [MaxLength(150)]
        public string? Department { get; set; }

        [MaxLength(500)]
        public string? OfficeAddress { get; set; }

        // Navigation
        public ICollection<ClientUser> ClientUsers { get; set; } = new List<ClientUser>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CLIENT  (engagement / institution)
    // ═══════════════════════════════════════════════════════════════════════════
    public class Client
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = "";            // e.g. TUT Enterprise Holdings

        [MaxLength(20)]
        public string FiscalYear { get; set; } = "";      // e.g. FY2024

        [MaxLength(500)]
        public string? Description { get; set; }

        // Status: Pending | Active | Closed
        [MaxLength(20)]
        public string Status { get; set; } = "Active";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(450)]
        public string CreatedByUserId { get; set; } = "";

        public bool IsActive { get; set; } = true;

        // Navigation
        public ICollection<ClientUser> ClientUsers { get; set; } = new List<ClientUser>();
        public ICollection<ValidationRun> ValidationRuns { get; set; } = new List<ValidationRun>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CLIENT USER  (many-to-many: users assigned to clients)
    // ═══════════════════════════════════════════════════════════════════════════
    public class ClientUser
    {
        public int Id { get; set; }

        public int ClientId { get; set; }
        public Client Client { get; set; } = null!;

        [MaxLength(450)]
        public string UserId { get; set; } = "";
        public ApplicationUser User { get; set; } = null!;

        // Role within this engagement: DataAnalyst | Manager | Director | Trainee
        [MaxLength(50)]
        public string EngagementRole { get; set; } = "DataAnalyst";

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(450)]
        public string AssignedByUserId { get; set; } = "";

        public bool IsActive { get; set; } = true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // VALIDATION RUN  (persists each Rule 36 execution)
    // ═══════════════════════════════════════════════════════════════════════════
    public class ValidationRun
    {
        public int Id { get; set; }

        public int ClientId { get; set; }
        public Client Client { get; set; } = null!;

        public int RuleNumber { get; set; } = 36;

        [MaxLength(100)]
        public string RuleName { get; set; } = "Deceased Students Validation";

        // Connection config used
        [MaxLength(200)]
        public string HemisServer { get; set; } = "";

        [MaxLength(200)]
        public string HemisDatabase { get; set; } = "";

        [MaxLength(100)]
        public string StudTable { get; set; } = "";

        [MaxLength(100)]
        public string DeceasedTable { get; set; } = "";

        [MaxLength(100)]
        public string StudColumn { get; set; } = "";

        [MaxLength(100)]
        public string DeceasedColumn { get; set; } = "";

        // Results
        public int TotalValidated { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal ExceptionRate { get; set; }

        // Pass | Fail
        [MaxLength(10)]
        public string Status { get; set; } = "";

        // Serialised exception records (JSON)
        public string? ExceptionsJson { get; set; }

        public DateTime RunAt { get; set; } = DateTime.UtcNow;

        [MaxLength(450)]
        public string RunByUserId { get; set; } = "";

        [MaxLength(100)]
        public string? RunByUserName { get; set; }

        public bool IsCurrent { get; set; } = true;

        public string? ResultsJson { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AUDIT LOG  (every significant action)
    // ═══════════════════════════════════════════════════════════════════════════
    public class AuditLog
    {
        public int Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [MaxLength(450)]
        public string? UserId { get; set; }

        [MaxLength(256)]
        public string? UserName { get; set; }

        // login | logout | create_client | assign_user | remove_user
        // run_validation | download | invite_user | deactivate_user
        [MaxLength(50)]
        public string Action { get; set; } = "";

        [MaxLength(500)]
        public string? Details { get; set; }

        [MaxLength(45)]
        public string? IpAddress { get; set; }
    }
}
