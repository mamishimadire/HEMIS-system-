using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule20TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoQualTable { get; set; }
        public string? AutoCregTable { get; set; }
        public string? AutoCrseTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule20VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_CRED";
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string PgTypesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75";
        public List<string> GoverningPartCodes { get; set; } = ["ALL"];
    }

    public class Rule20VerifyResult
    {
        public bool Success { get; set; }
        public int FoundationStudentCount { get; set; }
        public int ValidatedRowCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule20ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_CRED";
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string PgTypesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75";
        public List<string> GoverningPartCodes { get; set; } = ["ALL"];
    }

    public class Rule20PartSummaryItemViewModel
    {
        public string PartCode { get; set; } = "";
        public string PartTitle { get; set; } = "";
        public string PartDescription { get; set; } = "";
        public int TotalCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public string Status { get; set; } = "";
    }

    public class Rule20ReviewRowViewModel
    {
        public int ValidationNumber { get; set; }
        public string PartCode { get; set; } = "";
        public string PartTitle { get; set; } = "";
        public string PartDescription { get; set; } = "";
        public string StudentNumber007 { get; set; } = "";
        public string QualificationCode001 { get; set; } = "";
        public string Name019 { get; set; } = "";
        public string IdNumber024 { get; set; } = "";
        public string FoundationFlag106 { get; set; } = "";
        public string QualificationDescription003 { get; set; } = "";
        public string QualificationType005 { get; set; } = "";
        public string BridgeQualificationCode001 { get; set; } = "";
        public string CourseCode030 { get; set; } = "";
        public string CrseCourseCode030 { get; set; } = "";
        public string FoundationCourse091 { get; set; } = "";
        public string StudentType { get; set; } = "";
        public string NotebookStatus { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public string ValidationExplanation { get; set; } = "";
    }

    public class Rule20ValidationSummary
    {
        public bool Success { get; set; }
        public int FoundationStudentCount { get; set; }
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
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_CRED";
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string PgTypesText { get; set; } = "";
        public List<string> PgTypes { get; set; } = new();
        public List<string> GoverningPartCodes { get; set; } = ["ALL"];
        public string GoverningPartCodesText { get; set; } = "All Students";
        public string OverallStatusRuleText { get; set; } = "Overall PASS requires the filtered all-student population to PASS.";
        public string TableLinkageText { get; set; } = "";
        public List<string> ProcedureSteps { get; set; } = new();
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule20PartSummaryItemViewModel> PartSummaries { get; set; } = new();
        public List<Rule20ReviewRowViewModel> ReviewRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule20RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule20ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule20WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string CregTable { get; set; } = "dbo_CRED";
        public string CrseTable { get; set; } = "dbo_CRSE";
        public string PgTypesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75";
        public List<string> GoverningPartCodes { get; set; } = ["ALL"];
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule20ValidationSummary? Summary { get; set; }
    }

    public class Rule20WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule20WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule20RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule20WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule20SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}


