using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule22TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoProfTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule22ColumnRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string ProfTable { get; set; } = "";
    }

    public class Rule22ColumnResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoColumn041 { get; set; }
        public string? AutoColumn039 { get; set; }
        public string? Error { get; set; }
    }

    public class Rule22VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string ProfTable { get; set; } = "";
        public string Column041 { get; set; } = "_041";
        public string Column039 { get; set; } = "_039";
        public int Control1SampleSize { get; set; }
        public int Control2SampleSize { get; set; }
        public int Control3SampleSize { get; set; }
    }

    public class Rule22VerifyResult
    {
        public bool Success { get; set; }
        public int TotalCount { get; set; }
        public int Control1Count { get; set; }
        public int Control2Count { get; set; }
        public int Control3Count { get; set; }
        public int UnclassifiedCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule22ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ProfTable { get; set; } = "";
        public string Column041 { get; set; } = "_041";
        public string Column039 { get; set; } = "_039";
        public int Control1SampleSize { get; set; }
        public int Control2SampleSize { get; set; }
        public int Control3SampleSize { get; set; }
    }

    public class Rule22ControlSummaryItemViewModel
    {
        public string ControlType { get; set; } = "";
        public string ControlDefinition { get; set; } = "";
        public int AvailableCount { get; set; }
        public int RequestedCount { get; set; }
        public int SampleCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
    }

    public class Rule22ReviewRowViewModel
    {
        public int ValidationNumber { get; set; }
        public string ControlType { get; set; } = "";
        public string ControlDefinition { get; set; } = "";
        public int SampleNumber { get; set; }
        public string StaffNumber037 { get; set; } = "";
        public string Year038 { get; set; } = "";
        public string Col039 { get; set; } = "";
        public string Col040 { get; set; } = "";
        public string Col041 { get; set; } = "";
        public string Col042 { get; set; } = "";
        public string Col046 { get; set; } = "";
        public string Col047 { get; set; } = "";
        public string Col048 { get; set; } = "";
        public string Col094 { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ExceptionReason { get; set; } = "";
    }

    public class Rule22ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string ProfTable { get; set; } = "";
        public string Column041 { get; set; } = "_041";
        public string Column039 { get; set; } = "_039";
        public int Control1SampleSize { get; set; }
        public int Control2SampleSize { get; set; }
        public int Control3SampleSize { get; set; }
        public int Control1Count { get; set; }
        public int Control2Count { get; set; }
        public int Control3Count { get; set; }
        public int UnclassifiedCount { get; set; }
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule22ControlSummaryItemViewModel> ControlSummaries { get; set; } = new();
        public List<Rule22ReviewRowViewModel> ReviewRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule22RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule22ValidationSummary Summary { get; set; } = new();
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

    public class Rule22WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ProfTable { get; set; } = "";
        public string Column041 { get; set; } = "_041";
        public string Column039 { get; set; } = "_039";
        public int Control1SampleSize { get; set; }
        public int Control2SampleSize { get; set; }
        public int Control3SampleSize { get; set; }
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public Rule22ValidationSummary? Summary { get; set; }
    }

    public class Rule22WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule22WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule22RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule22WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule22SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
