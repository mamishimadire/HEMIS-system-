using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule11TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoQualTable { get; set; }
        public string? Error { get; set; }

        // Compatibility aliases used by shared UI helpers.
        public string? AutoStudTable
        {
            get => AutoQualTable;
            set => AutoQualTable = value;
        }

        public string? AutoCregTable
        {
            get => AutoQualTable;
            set => AutoQualTable = value;
        }

        public string? AutoCrseTable
        {
            get => AutoQualTable;
            set => AutoQualTable = value;
        }
    }

    public class Rule11VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string QualTable { get; set; } = "dbo_QUAL";
        public bool ExcludeMPrefixPattern { get; set; } = true;
        public string PostgraduateTypesCsv { get; set; } = "07,27,28,49,72,73,08,30,50,74,75";

        public string StudTable
        {
            get => QualTable;
            set => QualTable = value;
        }

        public string CregTable
        {
            get => QualTable;
            set => QualTable = value;
        }

        public string CrseTable
        {
            get => QualTable;
            set => QualTable = value;
        }

        public string BridgeTable
        {
            get => QualTable;
            set => QualTable = value;
        }
    }

    public class Rule11VerifyResult
    {
        public bool Success { get; set; }
        public int QualRecordCount { get; set; }
        public int ApprovedQualificationCount { get; set; }
        public int UndergraduateCount { get; set; }
        public int PostgraduateCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule11ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string QualTable { get; set; } = "dbo_QUAL";
        public bool ExcludeMPrefixPattern { get; set; } = true;
        public string PostgraduateTypesCsv { get; set; } = "07,27,28,49,72,73,08,30,50,74,75";

        public string StudTable
        {
            get => QualTable;
            set => QualTable = value;
        }

        public string CregTable
        {
            get => QualTable;
            set => QualTable = value;
        }

        public string CrseTable
        {
            get => QualTable;
            set => QualTable = value;
        }

        public string BridgeTable
        {
            get => QualTable;
            set => QualTable = value;
        }
    }

    public class Rule11ControlSummaryItemViewModel
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

    public class Rule11ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string ControlType { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ValidationExplanation { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule11ValidationSummary
    {
        public bool Success { get; set; }
        public int QualRecordCount { get; set; }
        public int ApprovedQualificationCount { get; set; }
        public int UndergraduateCount { get; set; }
        public int PostgraduateCount { get; set; }
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
        public string QualTable { get; set; } = "dbo_QUAL";
        public bool ExcludeMPrefixPattern { get; set; } = true;
        public string PostgraduateTypesCsv { get; set; } = "07,27,28,49,72,73,08,30,50,74,75";
        public string TableLinkageText { get; set; } = "";
        public string RuleModeText { get; set; } = "";
        public List<string> ProcedureSteps { get; set; } = new();
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule11ControlSummaryItemViewModel> ControlSummaries { get; set; } = new();
        public List<Rule11ValidationRowRecord> ReviewRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }

        public string StudTable
        {
            get => QualTable;
            set => QualTable = value;
        }

        public string CregTable
        {
            get => QualTable;
            set => QualTable = value;
        }

        public string CrseTable
        {
            get => QualTable;
            set => QualTable = value;
        }

        public string BridgeTable
        {
            get => QualTable;
            set => QualTable = value;
        }
    }

    public class Rule11RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule11ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule11WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string QualTable { get; set; } = "dbo_QUAL";
        public bool ExcludeMPrefixPattern { get; set; } = true;
        public string PostgraduateTypesCsv { get; set; } = "07,27,28,49,72,73,08,30,50,74,75";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule11ValidationSummary? Summary { get; set; }

        public string StudTable
        {
            get => QualTable;
            set => QualTable = value;
        }

        public string CregTable
        {
            get => QualTable;
            set => QualTable = value;
        }

        public string CrseTable
        {
            get => QualTable;
            set => QualTable = value;
        }

        public string BridgeTable
        {
            get => QualTable;
            set => QualTable = value;
        }
    }

    public class Rule11WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule11WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule11RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule11WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule11SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
