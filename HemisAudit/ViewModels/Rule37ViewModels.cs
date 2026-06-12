using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    // ═══════════════════════════════════════════════════════════════════════════
    // RULE 37 – CESM vs PQM VALIDATION
    // ═══════════════════════════════════════════════════════════════════════════

    public class Rule37ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        // CESM table (_001, _006)
        public string CesmTable { get; set; } = "";
        public string CesmIdCol { get; set; } = "";
        public string CesmCodeCol { get; set; } = "";
        // QUAL table (_001, _003)
        public string QualTable { get; set; } = "";
        public string QualIdCol { get; set; } = "";
        public string QualNameCol { get; set; } = "";
        // PQM table
        public string PqmTable { get; set; } = "";
        public string PqmNameCol { get; set; } = "";
        public string PqmCode1Col { get; set; } = "";
        public string PqmCode2Col { get; set; } = "";
    }

    public class Rule37ValidationRow
    {
        public int ValidationNumber { get; set; }
        public string RecordId { get; set; } = "";
        public string HemisCesmCode { get; set; } = "";
        public string HemisQualName { get; set; } = "";
        public string? PqmCode { get; set; }
        public string? PqmName { get; set; }
        public bool CodeMatch { get; set; }
        public bool NameMatch { get; set; }
        public bool NeedsReview { get; set; }
        public string ValidationResult { get; set; } = "";
        public string? ExceptionReason { get; set; }
    }

    public class Rule37ExceptionRecord
    {
        public int ValidationNumber { get; set; }
        public string RecordId { get; set; } = "";
        public string HemisCesmCode { get; set; } = "";
        public string HemisQualName { get; set; } = "";
        public string? PqmCode { get; set; }
        public string? PqmName { get; set; }
        public bool CodeMatch { get; set; }
        public bool NameMatch { get; set; }
        public bool NeedsReview { get; set; }
        public string ValidationResult { get; set; } = "";
        public string ExceptionReason { get; set; } = "";
    }

    public class Rule37ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int ReviewCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string CesmTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string PqmTable { get; set; } = "";
        public string CesmIdCol { get; set; } = "";
        public string CesmCodeCol { get; set; } = "";
        public string QualIdCol { get; set; } = "";
        public string QualNameCol { get; set; } = "";
        public string PqmNameCol { get; set; } = "";
        public string PqmCode1Col { get; set; } = "";
        public string PqmCode2Col { get; set; } = "";
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule37ValidationRow> ValidationRows { get; set; } = new();
        public List<Rule37ExceptionRecord> Exceptions { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule37VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string CesmTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string PqmTable { get; set; } = "";
        public string CesmIdCol { get; set; } = "";
        public string QualIdCol { get; set; } = "";
    }

    public class Rule37VerifyResult
    {
        public bool Success { get; set; }
        public int CesmTotal { get; set; }
        public int QualTotal { get; set; }
        public int PqmTotal { get; set; }
        public int MergedTotal { get; set; }
        public string? Error { get; set; }
    }

    public class Rule37TableListResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoCesmTable { get; set; }
        public string? AutoQualTable { get; set; }
        public string? AutoPqmTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule37GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
        public string TableRole { get; set; } = "";   // "cesm" | "qual" | "pqm"
    }

    public class Rule37WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string CesmTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string PqmTable { get; set; } = "";
        public string CesmIdCol { get; set; } = "";
        public string CesmCodeCol { get; set; } = "";
        public string QualIdCol { get; set; } = "";
        public string QualNameCol { get; set; } = "";
        public string PqmNameCol { get; set; } = "";
        public string PqmCode1Col { get; set; } = "";
        public string PqmCode2Col { get; set; } = "";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule37ValidationSummary? Summary { get; set; }
    }

    public class Rule37WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule37WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule37RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule37ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff =>
            IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload =>
            ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule37RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule37WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }
}
