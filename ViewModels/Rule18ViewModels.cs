using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule18TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoBridgeTable { get; set; }
        public string? AutoCregTable
        {
            get => AutoBridgeTable;
            set => AutoBridgeTable = value;
        }
        public string? AutoCrseTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule18VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string BridgeTable { get; set; } = "dbo_CREG";
        public string CregTable
        {
            get => BridgeTable;
            set => BridgeTable = value;
        }
        public string CrseTable { get; set; } = "dbo_CRSE";
    }

    public class Rule18VerifyResult
    {
        public bool Success { get; set; }
        public int StudRecordCount { get; set; }
        public int BridgeRecordCount { get; set; }
        public int CrseRecordCount { get; set; }
        public int NsfasPopulationCount { get; set; }
        public int Control1PopulationCount { get; set; }
        public int Control2PopulationCount { get; set; }
        public int Control3PopulationCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule18ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string BridgeTable { get; set; } = "dbo_CREG";
        public string CregTable
        {
            get => BridgeTable;
            set => BridgeTable = value;
        }
        public string CrseTable { get; set; } = "dbo_CRSE";
        public int SampleControl1Size { get; set; } = 5;
        public int SampleControl2Size { get; set; } = 3;
        public int SampleControl3Size { get; set; } = 32;
    }

    public class Rule18ControlSummaryItemViewModel
    {
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string CriteriaText { get; set; } = "";
        public int RequestedCount { get; set; }
        public int AvailableCount { get; set; }
        public int AchievedCount { get; set; }
        public int TotalCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public string Status { get; set; } = "";
    }

    public class Rule18ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ValidationExplanation { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule18ValidationSummary
    {
        public bool Success { get; set; }
        public int StudRecordCount { get; set; }
        public int BridgeRecordCount { get; set; }
        public int CrseRecordCount { get; set; }
        public int NsfasPopulationCount { get; set; }
        public int TotalRequested { get; set; }
        public int TotalValidated { get; set; }
        public int DisplayedCount { get; set; }
        public bool IsPreviewOnly { get; set; }
        public int PreviewLimit { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string StudTable { get; set; } = "dbo_STUD";
        public string BridgeTable { get; set; } = "dbo_CREG";
        public string CregTable
        {
            get => BridgeTable;
            set => BridgeTable = value;
        }
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string TableLinkageText { get; set; } = "";
        public string RuleModeText { get; set; } = "";
        public List<string> ProcedureSteps { get; set; } = new();
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule18ControlSummaryItemViewModel> ControlSummaries { get; set; } = new();
        public List<Rule18ValidationRowRecord> ReviewRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule18RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule18ValidationSummary Summary { get; set; } = new();
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

    public class Rule18WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string BridgeTable { get; set; } = "dbo_CREG";
        public string CregTable
        {
            get => BridgeTable;
            set => BridgeTable = value;
        }
        public string CrseTable { get; set; } = "dbo_CRSE";
        public int SampleControl1Size { get; set; } = 5;
        public int SampleControl2Size { get; set; } = 3;
        public int SampleControl3Size { get; set; } = 32;
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public Rule18ValidationSummary? Summary { get; set; }
    }

    public class Rule18WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule18WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule18RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule18WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule18SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
