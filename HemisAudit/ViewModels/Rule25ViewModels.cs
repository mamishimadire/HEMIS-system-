using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule25TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoCrseTable { get; set; }
        public string? AutoAuditTable { get; set; }
        public string? AutoH16Table { get; set; }
        public string? Error { get; set; }
    }

    public class Rule25AuditColumnRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string AuditTable { get; set; } = "";
    }

    public class Rule25AuditColumnResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoCourseCodeColumn { get; set; }
        public string? Error { get; set; }
    }

    public class Rule25VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string CrseTable { get; set; } = "";
        public string AuditTable { get; set; } = "";
        public string H16Table { get; set; } = "";
        public string CrseCourseCodeColumn { get; set; } = "_030";
        public string AuditCourseCodeColumn { get; set; } = "IALSUBJ";
        public string H16CourseCodeColumn { get; set; } = "_030";
    }

    public class Rule25VerifyResult
    {
        public bool Success { get; set; }
        public int CrseCount { get; set; }
        public int AuditCount { get; set; }
        public int H16Count { get; set; }
        public string? Error { get; set; }
    }

    public class Rule25ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string CrseTable { get; set; } = "";
        public string AuditTable { get; set; } = "";
        public string H16Table { get; set; } = "";
        public string CrseCourseCodeColumn { get; set; } = "_030";
        public string AuditCourseCodeColumn { get; set; } = "IALSUBJ";
        public string H16CourseCodeColumn { get; set; } = "_030";
    }

    public class Rule25IssueBreakdownItemViewModel
    {
        public string Status { get; set; } = "";
        public int Count { get; set; }
    }

    public class Rule25ReconciliationRowViewModel
    {
        public int ValidationNumber { get; set; }
        public string CrseCourseCode { get; set; } = "";
        public string AuditCourseCode { get; set; } = "";
        public string H16CourseCode { get; set; } = "";
        public string ReconciliationStatus { get; set; } = "";
        public string IssueDescription { get; set; } = "";
    }

    public class Rule25ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int Matches { get; set; }
        public int Mismatches { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public decimal MatchRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string CrseTable { get; set; } = "";
        public string AuditTable { get; set; } = "";
        public string H16Table { get; set; } = "";
        public string CrseCourseCodeColumn { get; set; } = "_030";
        public string AuditCourseCodeColumn { get; set; } = "IALSUBJ";
        public string H16CourseCodeColumn { get; set; } = "_030";
        public int CrseCount { get; set; }
        public int AuditCount { get; set; }
        public int H16Count { get; set; }
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public int PassSampleCount { get; set; }
        public bool PassSampleTruncated { get; set; }
        public List<Rule25IssueBreakdownItemViewModel> IssueCounts { get; set; } = new();
        public List<Rule25ReconciliationRowViewModel> PassSampleRows { get; set; } = new();
        public List<Rule25ReconciliationRowViewModel> FailRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule25RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule25ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule25WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string CrseTable { get; set; } = "";
        public string AuditTable { get; set; } = "";
        public string H16Table { get; set; } = "";
        public string CrseCourseCodeColumn { get; set; } = "_030";
        public string AuditCourseCodeColumn { get; set; } = "IALSUBJ";
        public string H16CourseCodeColumn { get; set; } = "_030";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule25ValidationSummary? Summary { get; set; }
    }

    public class Rule25WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule25WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule25RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule25WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule25SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}


