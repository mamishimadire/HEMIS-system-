using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule35GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
    }

    public class Rule35ColumnSelectionResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoDuplicateColumn { get; set; }
        public string? Error { get; set; }
    }

    public class Rule35VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
        public string DuplicateColumn { get; set; } = "";
    }

    public class Rule35SampleRowViewModel
    {
        public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule35VerifyResult
    {
        public bool Success { get; set; }
        public int TotalRecords { get; set; }
        public int NonNullValues { get; set; }
        public int DistinctValues { get; set; }
        public int DuplicateValues { get; set; }
        public int DuplicateRecords { get; set; }
        public List<Rule35SampleRowViewModel> SampleRows { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule35ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
        public string DuplicateColumn { get; set; } = "";
    }

    public class Rule35DuplicateSummaryItemViewModel
    {
        public string Value { get; set; } = "";
        public int Count { get; set; }
    }

    public class Rule35ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string DuplicateValue { get; set; } = "";
        public int OccurrenceCount { get; set; }
        public string DuplicateStatus { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule35ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int UniqueValues { get; set; }
        public int DuplicateValues { get; set; }
        public int DuplicateRecords { get; set; }
        public int DisplayedCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string TableName { get; set; } = "";
        public string DuplicateColumn { get; set; } = "";
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule35DuplicateSummaryItemViewModel> DuplicateSummary { get; set; } = new();
        public List<Rule35ValidationRowRecord> ValidationRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule35RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule35ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule35WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
        public string DuplicateColumn { get; set; } = "";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule35ValidationSummary? Summary { get; set; }
    }

    public class Rule35WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule35WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule35RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule35WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule35SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
