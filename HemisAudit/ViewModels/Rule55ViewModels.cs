using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    // ═══════════════════════════════════════════════════════════════════════════
    // RULE 55 – GRADUATE W-CODE VALIDATION
    // Tables: dbo_STUD (_007, _001, _025) + dbo_QUAL (_001, _003, _005, _004)
    // Checks: STUD._025 = configurable filter value → QUAL row found AND QUAL._004 = configurable approval value
    // DHET §1.5: Element 025='W' graduates treated identically to 'F'
    // ═══════════════════════════════════════════════════════════════════════════

    public class Rule55ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        // STUD table
        public string StudTable { get; set; } = "";
        public string StudIdCol { get; set; } = "";
        public string StudQualCodeCol { get; set; } = "";
        public string StudFulfilledCol { get; set; } = "";
        public string StudFulfilledFilterValue { get; set; } = "W";
        // QUAL table
        public string QualTable { get; set; } = "";
        public string QualCodeCol { get; set; } = "";
        public string QualNameCol { get; set; } = "";
        public string QualTypeCol { get; set; } = "";
        public string QualApprovalCol { get; set; } = "";
        public string QualApprovalFilterValue { get; set; } = "A";
        public string PqmTable { get; set; } = "";
        public string PqmQualNameColumn { get; set; } = "";
        public string PqmQualTypeColumn { get; set; } = "";
    }

    public class Rule55ValidationRow
    {
        public int ValidationNumber { get; set; }
        public string StudentId { get; set; } = "";
        public string QualCode { get; set; } = "";
        public string FulfilledStatus { get; set; } = "";
        public string? QualName { get; set; }
        public string? QualType { get; set; }
        public string? QualApprovalStatus { get; set; }
        public string? PqmQualName { get; set; }
        public string? PqmQualType { get; set; }
        public bool NameMatch { get; set; }
        public bool TypeMatch { get; set; }
        public string ValidationResult { get; set; } = "";
        public string? ExceptionReason { get; set; }
    }

    public class Rule55ExceptionRecord
    {
        public int ValidationNumber { get; set; }
        public string StudentId { get; set; } = "";
        public string QualCode { get; set; } = "";
        public string FulfilledStatus { get; set; } = "";
        public string? QualName { get; set; }
        public string? QualType { get; set; }
        public string? QualApprovalStatus { get; set; }
        public string? PqmQualName { get; set; }
        public string? PqmQualType { get; set; }
        public bool NameMatch { get; set; }
        public bool TypeMatch { get; set; }
        public string ValidationResult { get; set; } = "";
        public string ExceptionReason { get; set; } = "";
    }

    public class Rule55ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string StudTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string StudIdCol { get; set; } = "";
        public string StudQualCodeCol { get; set; } = "";
        public string StudFulfilledCol { get; set; } = "";
        public string StudFulfilledFilterValue { get; set; } = "W";
        public string QualCodeCol { get; set; } = "";
        public string QualNameCol { get; set; } = "";
        public string QualTypeCol { get; set; } = "";
        public string QualApprovalCol { get; set; } = "";
        public string QualApprovalFilterValue { get; set; } = "A";
        public string PqmTable { get; set; } = "";
        public string PqmQualNameColumn { get; set; } = "";
        public string PqmQualTypeColumn { get; set; } = "";
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule55ValidationRow> ValidationRows { get; set; } = new();
        public List<Rule55ExceptionRecord> Exceptions { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule55VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string StudTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string StudQualCodeCol { get; set; } = "";
        public string QualCodeCol { get; set; } = "";
        public string StudFulfilledCol { get; set; } = "";
        public string StudFulfilledFilterValue { get; set; } = "W";
        public string QualApprovalFilterValue { get; set; } = "A";
        public string PqmTable { get; set; } = "";
        public string PqmQualNameColumn { get; set; } = "";
        public string PqmQualTypeColumn { get; set; } = "";
    }

    public class Rule55VerifyResult
    {
        public bool Success { get; set; }
        public int StudTotal { get; set; }
        public int QualTotal { get; set; }
        public int FilteredTotal { get; set; }
        public string FilterColumn { get; set; } = "";
        public string FilterValue { get; set; } = "";
        public string? Error { get; set; }
    }

    public class Rule55TableListResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoQualTable { get; set; }
        public string? AutoPqmTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule55GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
        public string TableRole { get; set; } = "";
    }

    public class Rule55WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string StudIdCol { get; set; } = "";
        public string StudQualCodeCol { get; set; } = "";
        public string StudFulfilledCol { get; set; } = "";
        public string StudFulfilledFilterValue { get; set; } = "W";
        public string QualCodeCol { get; set; } = "";
        public string QualNameCol { get; set; } = "";
        public string QualTypeCol { get; set; } = "";
        public string QualApprovalCol { get; set; } = "";
        public string QualApprovalFilterValue { get; set; } = "A";
        public string PqmTable { get; set; } = "";
        public string PqmQualNameColumn { get; set; } = "";
        public string PqmQualTypeColumn { get; set; } = "";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule55ValidationSummary? Summary { get; set; }
    }

    public class Rule55WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule55WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule55RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule55ValidationSummary Summary { get; set; } = new();
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

    public class Rule55RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule55WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }
}


