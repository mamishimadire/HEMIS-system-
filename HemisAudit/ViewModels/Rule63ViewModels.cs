namespace HemisAudit.ViewModels
{
    public class Rule63ColumnMapping
    {
        public string StudStudentNoCol { get; set; } = "_007";
        public string StudQualCodeCol { get; set; } = "_001";
        public string CregStudentNoCol { get; set; } = "_007";
        public string CregQualCodeCol { get; set; } = "_001";
        public string QualQualCodeCol { get; set; } = "_001";
    }

    public class Rule63ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_CREG";
        public Rule63ColumnMapping ColumnMapping { get; set; } = new();
    }

    public class Rule63ReviewRow
    {
        public int RowNumber { get; set; }
        public string SourceTable { get; set; } = "";
        public string StudentNo { get; set; } = "";
        public string QualCode { get; set; } = "";
        public string QualJoinCode { get; set; } = "";
        public string ErrorCode { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ValidationExplanation { get; set; } = "";
    }

    public class Rule63ValidationSummary
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Warning { get; set; }
        public int? SavedRunId { get; set; }
        public int ClientId { get; set; }
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_CREG";
        public Rule63ColumnMapping ColumnMapping { get; set; } = new();
        public int TotalCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int ExceptionDetailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public bool IsPreviewOnly { get; set; }
        public int PreviewLimit { get; set; }
        public List<Rule63ReviewRow> PassRows { get; set; } = new();
        public List<Rule63ReviewRow> FailRows { get; set; } = new();
    }

    public class Rule63VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_CREG";
        public Rule63ColumnMapping ColumnMapping { get; set; } = new();
    }

    public class Rule63VerifyResult
    {
        public bool Success { get; set; }
        public int StudCount { get; set; }
        public int CregCount { get; set; }
        public int QualCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule63TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoQualTable { get; set; }
        public string? AutoCregTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule63ColumnDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule63WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule63WorkspaceStateViewModel? Workspace { get; set; }
    }

    public class Rule63WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_CREG";
        public Rule63ColumnMapping ColumnMapping { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule63ValidationSummary? Summary { get; set; }
    }

    public class Rule63RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string? GeneratedSql { get; set; }
        public Rule63ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule63SqlResult
    {
        public bool Success { get; set; }
        public string? Sql { get; set; }
        public string? Error { get; set; }
    }

    public class Rule63WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule63GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
    }
}
