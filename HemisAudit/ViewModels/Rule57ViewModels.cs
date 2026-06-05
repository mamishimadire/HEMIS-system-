using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    // ═══════════════════════════════════════════════════════════════════════════
    // RULE 57 – REGISTRATION DOCUMENTATION AGREEMENT
    // Tables: dbo_STUD (_007, _001, _024) + dbo_CREG (_007, _001, _064)
    // Checks: CREG._064 = filter value → STUD._024 agrees with CREG._064
    // Description: Agrees to the student's signed application and/or registration documentation.
    // ═══════════════════════════════════════════════════════════════════════════

    public class Rule57ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        // STUD table
        public string StudTable { get; set; } = "";
        public string StudIdCol { get; set; } = "";
        public string StudCodeCol { get; set; } = "";
        public string StudRegTypeCol { get; set; } = "";
        // CREG table
        public string CregTable { get; set; } = "";
        public string CregIdCol { get; set; } = "";
        public string CregCodeCol { get; set; } = "";
        public string CregRegTypeCol { get; set; } = "";
        public string CregRegTypeFilterValue { get; set; } = "";
    }

    public class Rule57ValidationRow
    {
        public int ValidationNumber { get; set; }
        public string StudentId { get; set; } = "";
        public string StudCode { get; set; } = "";
        public string StudRegType { get; set; } = "";
        public string? CregCode { get; set; }
        public string? CregRegType { get; set; }
        public string ValidationResult { get; set; } = "";
        public string? ExceptionReason { get; set; }
    }

    public class Rule57ExceptionRecord
    {
        public int ValidationNumber { get; set; }
        public string StudentId { get; set; } = "";
        public string StudCode { get; set; } = "";
        public string StudRegType { get; set; } = "";
        public string? CregCode { get; set; }
        public string? CregRegType { get; set; }
        public string ValidationResult { get; set; } = "";
        public string ExceptionReason { get; set; } = "";
    }

    public class Rule57ValidationSummary
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
        public string CregTable { get; set; } = "";
        public string StudIdCol { get; set; } = "";
        public string StudCodeCol { get; set; } = "";
        public string StudRegTypeCol { get; set; } = "";
        public string CregIdCol { get; set; } = "";
        public string CregCodeCol { get; set; } = "";
        public string CregRegTypeCol { get; set; } = "";
        public string CregRegTypeFilterValue { get; set; } = "";
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule57ValidationRow> ValidationRows { get; set; } = new();
        public List<Rule57ExceptionRecord> Exceptions { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule57VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string StudTable { get; set; } = "";
        public string CregTable { get; set; } = "";
        public string CregRegTypeCol { get; set; } = "";
        public string CregRegTypeFilterValue { get; set; } = "";
    }

    public class Rule57VerifyResult
    {
        public bool Success { get; set; }
        public int StudTotal { get; set; }
        public int CregTotal { get; set; }
        public int FilteredTotal { get; set; }
        public string FilterColumn { get; set; } = "";
        public string FilterValue { get; set; } = "";
        public string? Error { get; set; }
    }

    public class Rule57TableListResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoCregTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule57GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
        public string TableRole { get; set; } = "";
    }

    public class Rule57WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "";
        public string CregTable { get; set; } = "";
        public string StudIdCol { get; set; } = "";
        public string StudCodeCol { get; set; } = "";
        public string StudRegTypeCol { get; set; } = "";
        public string CregIdCol { get; set; } = "";
        public string CregCodeCol { get; set; } = "";
        public string CregRegTypeCol { get; set; } = "";
        public string CregRegTypeFilterValue { get; set; } = "";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule57ValidationSummary? Summary { get; set; }
    }

    public class Rule57WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule57WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule57RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule57ValidationSummary Summary { get; set; } = new();
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

    public class Rule57RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule57WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }
}
