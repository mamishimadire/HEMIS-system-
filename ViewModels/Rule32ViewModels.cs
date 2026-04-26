using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule32GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
    }

    public class Rule32ColumnSelectionResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoErrorTypeColumn { get; set; }
        public string? AutoErrorColumn { get; set; }
        public string? Error { get; set; }
    }

    public class Rule32FilterValueOption
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        public int Count { get; set; }
    }

    public class Rule32FilterValueRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ErrorTypeColumn { get; set; } = "";
    }

    public class Rule32FilterValueResult
    {
        public bool Success { get; set; }
        public List<Rule32FilterValueOption> Options { get; set; } = new();
        public string? DefaultValue { get; set; }
        public string? Error { get; set; }
    }

    public class Rule32VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ErrorTypeColumn { get; set; } = "";
        public string ErrorColumn { get; set; } = "";
        public string ErrorTypeValue { get; set; } = "";
        public string ExclusionCodes { get; set; } = "";
    }

    public class Rule32VerifyResult
    {
        public bool Success { get; set; }
        public int TotalRecords { get; set; }
        public int TotalFatal { get; set; }
        public int ExcludedCount { get; set; }
        public int RemainingCount { get; set; }
        public List<string> NormalizedExclusions { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule32ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
        public string ErrorTypeColumn { get; set; } = "";
        public string ErrorColumn { get; set; } = "";
        public string ErrorTypeValue { get; set; } = "Fatal";
        public string ExclusionCodes { get; set; } = "";
    }

    public class Rule32BreakdownItemViewModel
    {
        public string ErrorCode { get; set; } = "";
        public int Count { get; set; }
    }

    public class Rule32ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string ErrorTypeValue { get; set; } = "";
        public string ErrorCode { get; set; } = "";
        public string NormalizedErrorCode { get; set; } = "";
        public string Classification { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public string Description { get; set; } = "";
        public string ElementInformation { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule32ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int TotalFatal { get; set; }
        public int ExcludedCount { get; set; }
        public int RemainingCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ErrorTypeColumn { get; set; } = "";
        public string ErrorColumn { get; set; } = "";
        public string ErrorTypeValue { get; set; } = "";
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<string> Exclusions { get; set; } = new();
        public List<string> NormalizedExclusions { get; set; } = new();
        public List<Rule32BreakdownItemViewModel> ExcludedBreakdown { get; set; } = new();
        public List<Rule32BreakdownItemViewModel> RemainingBreakdown { get; set; } = new();
        public List<Rule32ValidationRowRecord> ExcludedRows { get; set; } = new();
        public List<Rule32ValidationRowRecord> RemainingRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule32RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule32ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            !string.IsNullOrWhiteSpace(CurrentUserEngagementRole) &&
            Signoffs.Any(s =>
                s.IsCurrentUser &&
                string.Equals(s.SignoffRole, CurrentUserEngagementRole, StringComparison.OrdinalIgnoreCase));
        public bool CanCurrentUserSignOff => ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule32WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
        public string ErrorTypeColumn { get; set; } = "";
        public string ErrorColumn { get; set; } = "";
        public string ErrorTypeValue { get; set; } = "Fatal";
        public string ExclusionCodes { get; set; } = "";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public Rule32ValidationSummary? Summary { get; set; }
    }

    public class Rule32WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule32WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule32RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule32WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule32SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
