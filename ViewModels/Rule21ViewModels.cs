using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule21GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
    }

    public class Rule21ColumnSelectionResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoFilterColumn { get; set; }
        public string? AutoBreakdownColumn { get; set; }
        public string? Error { get; set; }
    }

    public class Rule21FilterValueRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
        public string FilterColumn { get; set; } = "";
    }

    public class Rule21FilterValueOption
    {
        public string Value { get; set; } = "";
        public int Count { get; set; }
        public string Label { get; set; } = "";
    }

    public class Rule21FilterValueResult
    {
        public bool Success { get; set; }
        public List<Rule21FilterValueOption> Options { get; set; } = new();
        public string? DefaultValue { get; set; }
        public string? Error { get; set; }
    }

    public class Rule21VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
        public string FilterColumn { get; set; } = "";
        public string FilterValue { get; set; } = "";
    }

    public class Rule21SampleRowViewModel
    {
        public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule21VerifyResult
    {
        public bool Success { get; set; }
        public int TotalRecords { get; set; }
        public int MatchingCount { get; set; }
        public List<Rule21SampleRowViewModel> SampleRows { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule21ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "dbo_STUD";
        public string FilterColumn { get; set; } = "_010";
        public string FilterValue { get; set; } = "F";
        public string BreakdownColumn { get; set; } = "";
        public int SampleSize { get; set; } = 1;
        public bool ShowAllRecords { get; set; } = true;
    }

    public class Rule21BreakdownItemViewModel
    {
        public string Value { get; set; } = "";
        public int Count { get; set; }
    }

    public class Rule21ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string FilterValue { get; set; } = "";
        public string BreakdownValue { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule21ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int MatchingCount { get; set; }
        public int DisplayedCount { get; set; }
        public bool IsPreviewOnly { get; set; }
        public int PreviewLimit { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string TableName { get; set; } = "";
        public string FilterColumn { get; set; } = "";
        public string FilterValue { get; set; } = "";
        public string BreakdownColumn { get; set; } = "";
        public int SampleSize { get; set; }
        public bool ShowAllRecords { get; set; }
        public bool Sampled { get; set; }
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule21BreakdownItemViewModel> Breakdown { get; set; } = new();
        public List<Rule21ValidationRowRecord> MatchingRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule21RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule21ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            !string.IsNullOrWhiteSpace(CurrentUserEngagementRole) &&
            Signoffs.Any(s =>
                s.IsCurrentUser &&
                string.Equals(s.SignoffRole, CurrentUserEngagementRole, StringComparison.OrdinalIgnoreCase));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule21WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "dbo_STUD";
        public string FilterColumn { get; set; } = "_010";
        public string FilterValue { get; set; } = "F";
        public string BreakdownColumn { get; set; } = "";
        public int SampleSize { get; set; } = 1;
        public bool ShowAllRecords { get; set; } = true;
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public Rule21ValidationSummary? Summary { get; set; }
    }

    public class Rule21WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule21WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule21RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule21WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule21SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
