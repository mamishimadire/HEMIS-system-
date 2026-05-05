using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule12TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoCregTable { get; set; }
        public string? AutoCrseTable { get; set; }
        public string? AutoStudTable
        {
            get => AutoCregTable;
            set => AutoCregTable = value;
        }

        public string? AutoBridgeTable
        {
            get => AutoCrseTable;
            set => AutoCrseTable = value;
        }
        public string? Error { get; set; }
    }

    public class Rule12VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string CregTable { get; set; } = "dbo_CREG";
        public string CrseTable { get; set; } = "dbo_CRSE";

        // Backward-compatible aliases for the shared workspace storage columns.
        public string StudTable
        {
            get => CregTable;
            set => CregTable = value;
        }

        public string BridgeTable
        {
            get => CrseTable;
            set => CrseTable = value;
        }
    }

    public class Rule12VerifyResult
    {
        public bool Success { get; set; }
        public int CregRecordCount { get; set; }
        public int CrseRecordCount { get; set; }
        public int TotalCoursesSelected { get; set; }
        public int MatchedCourseCount { get; set; }
        public int MissingCourseCount { get; set; }

        public int StudRecordCount
        {
            get => CregRecordCount;
            set => CregRecordCount = value;
        }

        public int BridgeRecordCount
        {
            get => CrseRecordCount;
            set => CrseRecordCount = value;
        }

        public int ApprovedCourseCount
        {
            get => TotalCoursesSelected;
            set => TotalCoursesSelected = value;
        }

        public int RegisteredCourseCount
        {
            get => MatchedCourseCount;
            set => MatchedCourseCount = value;
        }

        public int MissingRegistrationCount
        {
            get => MissingCourseCount;
            set => MissingCourseCount = value;
        }

        public string? Error { get; set; }
    }

    public class Rule12ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string CregTable { get; set; } = "dbo_CREG";
        public string CrseTable { get; set; } = "dbo_CRSE";

        public string StudTable
        {
            get => CregTable;
            set => CregTable = value;
        }

        public string BridgeTable
        {
            get => CrseTable;
            set => CrseTable = value;
        }
    }

    public class Rule12ControlSummaryItemViewModel
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

    public class Rule12ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ValidationExplanation { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule12ValidationSummary
    {
        public bool Success { get; set; }
        public int CregRecordCount { get; set; }
        public int CrseRecordCount { get; set; }
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
        public string CregTable { get; set; } = "dbo_CREG";
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string TableLinkageText { get; set; } = "";
        public string RuleModeText { get; set; } = "";
        public List<string> ProcedureSteps { get; set; } = new();
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule12ControlSummaryItemViewModel> ControlSummaries { get; set; } = new();
        public List<Rule12ValidationRowRecord> ReviewRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }

        public string StudTable
        {
            get => CregTable;
            set => CregTable = value;
        }

        public string BridgeTable
        {
            get => CrseTable;
            set => CrseTable = value;
        }

        public int ApprovedCourseCount
        {
            get => TotalValidated;
            set => TotalValidated = value;
        }

        public int RegisteredCourseCount
        {
            get => PassCount;
            set => PassCount = value;
        }

        public int MissingRegistrationCount => FailCount;
    }

    public class Rule12RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule12ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule12WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string CregTable { get; set; } = "dbo_CREG";
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule12ValidationSummary? Summary { get; set; }

        public string StudTable
        {
            get => CregTable;
            set => CregTable = value;
        }

        public string BridgeTable
        {
            get => CrseTable;
            set => CrseTable = value;
        }
    }

    public class Rule12WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule12WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule12RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule12WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule12SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
