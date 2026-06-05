using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule10TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoQualTable { get; set; }
        public string? AutoStudTable { get; set; }
        public string? AutoCregTable { get; set; }
        public string? AutoCrseTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule10GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
    }

    public class Rule10ColumnSelectionResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule10JoinDatasetConfigItem
    {
        public string DatasetCode { get; set; } = "";
        public string DatasetLabel { get; set; } = "";
        public string TableName { get; set; } = "";
        public List<string> KeyColumns { get; set; } = new();
        public List<string> CompositeKeyFields { get; set; } = new();
    }

    public class Rule10JoinDatasetVerificationItem
    {
        public string DatasetCode { get; set; } = "";
        public string DatasetLabel { get; set; } = "";
        public string TableName { get; set; } = "";
        public int RecordCount { get; set; }
        public List<string> RequiredColumns { get; set; } = new();
        public List<string> MissingColumns { get; set; } = new();
        public string Status { get; set; } = "";
    }

    public class Rule10VerifyRequest
    {
        public int RuleNumber { get; set; } = 10;
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string StudTable { get; set; } = "dbo_STUD";
        public string CregTable { get; set; } = "dbo_CREG";
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string QualColumn { get; set; } = "_005";
        public string StudColumn { get; set; } = "_007";
        public string CregColumn { get; set; } = "_030";
        public string CrseColumn { get; set; } = "_030";
        public string RuleParameterJson { get; set; } = "";
        public string Rule10JoinConfigJson { get; set; } = "";
        public string BridgeTable
        {
            get => CregTable;
            set => CregTable = value;
        }
    }

    public class Rule10VerifyResult
    {
        public bool Success { get; set; }
        public int QualRecordCount { get; set; }
        public int StudRecordCount { get; set; }
        public int CregRecordCount { get; set; }
        public int CrseRecordCount { get; set; }
        public List<Rule10JoinDatasetVerificationItem> JoinDatasets { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule10ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public int RuleNumber { get; set; } = 10;
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string StudTable { get; set; } = "dbo_STUD";
        public string CregTable { get; set; } = "dbo_CREG";
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string QualColumn { get; set; } = "_005";
        public string StudColumn { get; set; } = "_007";
        public string CregColumn { get; set; } = "_030";
        public string CrseColumn { get; set; } = "_030";
        public string RuleParameterJson { get; set; } = "";
        public string Rule10JoinConfigJson { get; set; } = "";
        public string BridgeTable
        {
            get => CregTable;
            set => CregTable = value;
        }
    }

    public class Rule10ControlSummaryItemViewModel
    {
        public int RuleId { get; set; }
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string CriteriaText { get; set; } = "";
        public string TableName { get; set; } = "";
        public string Severity { get; set; } = "";
        public int ErrorCount { get; set; }
        public int RequestedCount { get; set; }
        public int AvailableCount { get; set; }
        public int AchievedCount { get; set; }
        public int TotalCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public string Status { get; set; } = "";
    }

    public class Rule10ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public int RuleId { get; set; }
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ValidationExplanation { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule10ValidationSummary
    {
        public bool Success { get; set; }
        public int RuleNumber { get; set; } = 10;
        public string RuleLabel { get; set; } = "Rule 10";
        public string RuleTitle { get; set; } = "Integrity Check";
        public int QualRecordCount { get; set; }
        public int StudRecordCount { get; set; }
        public int CregRecordCount { get; set; }
        public int CrseRecordCount { get; set; }
        public int TotalChecks { get; set; }
        public int PassedChecks { get; set; }
        public int FailedChecks { get; set; }
        public int TotalIssues { get; set; }
        public int HighSeverityCount { get; set; }
        public int TotalRequested { get; set; }
        public int TotalValidated { get; set; }
        public int DisplayedCount { get; set; }
        public bool IsPreviewOnly { get; set; }
        public int PreviewLimit { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string OverallStatusText { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string StudTable { get; set; } = "dbo_STUD";
        public string CregTable { get; set; } = "dbo_CREG";
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string QualColumn { get; set; } = "_005";
        public string StudColumn { get; set; } = "_007";
        public string CregColumn { get; set; } = "_030";
        public string CrseColumn { get; set; } = "_030";
        public string RuleParameterJson { get; set; } = "";
        public string Rule10JoinConfigJson { get; set; } = "";
        public string BridgeTable
        {
            get => CregTable;
            set => CregTable = value;
        }
        public string TableLinkageText { get; set; } = "";
        public string RuleModeText { get; set; } = "";
        public List<string> ProcedureSteps { get; set; } = new();
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule10ControlSummaryItemViewModel> ControlSummaries { get; set; } = new();
        public List<Rule10ValidationRowRecord> ReviewRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule10RunReviewViewModel
    {
        public int RunId { get; set; }
        public int RuleNumber { get; set; } = 10;
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule10ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule10WorkspaceStateViewModel
    {
        public int RuleNumber { get; set; } = 10;
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string StudTable { get; set; } = "dbo_STUD";
        public string CregTable { get; set; } = "dbo_CREG";
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string QualColumn { get; set; } = "_005";
        public string StudColumn { get; set; } = "_007";
        public string CregColumn { get; set; } = "_030";
        public string CrseColumn { get; set; } = "_030";
        public string RuleParameterJson { get; set; } = "";
        public string Rule10JoinConfigJson { get; set; } = "";
        public string BridgeTable
        {
            get => CregTable;
            set => CregTable = value;
        }
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule10ValidationSummary? Summary { get; set; }
    }

    public class Rule10WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule10WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule10RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule10WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule10SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
