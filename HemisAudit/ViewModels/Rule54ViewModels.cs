using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    // ═══════════════════════════════════════════════════════════════════════════
    // RULE 54 – CRED vs QUAL vs PQM VALIDATION
    // Tables: dbo_CRED (_001,_030,_036,_050) + dbo_QUAL (_001,_003) + PQM
    // Checks: QUAL._003 == PQM.Authorised_Qualification_Name
    //         CRED._050 == PQM.Research_1  (both on same PQM row → PASS)
    // ═══════════════════════════════════════════════════════════════════════════

    public class Rule54ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        // CRED table
        public string CredTable { get; set; } = "";
        public string CredIdCol { get; set; } = "";
        public string CredCourseCol { get; set; } = "";
        public string CredCreditCol { get; set; } = "";
        public string CredResearch1Col { get; set; } = "";
        // QUAL table
        public string QualTable { get; set; } = "";
        public string QualIdCol { get; set; } = "";
        public string QualNameCol { get; set; } = "";
        // PQM table
        public string PqmTable { get; set; } = "";
        public string PqmNameCol { get; set; } = "";
        public string PqmResearch1Col { get; set; } = "";
    }

    public class Rule54ValidationRow
    {
        public int ValidationNumber { get; set; }
        public string RecordId { get; set; } = "";       // CRED._001
        public string QualRecordId { get; set; } = "";   // QUAL._001
        public string CourseCode { get; set; } = "";     // CRED._030
        public string CreditValue { get; set; } = "";    // CRED._036
        public string HemisResearch1 { get; set; } = ""; // CRED._050
        public string HemisQualName { get; set; } = "";  // QUAL._003
        public string? PqmQualName { get; set; }
        public string? PqmResearch1 { get; set; }
        public bool QualNameMatch { get; set; }
        public bool Research1Match { get; set; }
        public string ValidationResult { get; set; } = "";
        public string? ExceptionReason { get; set; }
    }

    public class Rule54ExceptionRecord
    {
        public int ValidationNumber { get; set; }
        public string RecordId { get; set; } = "";
        public string QualRecordId { get; set; } = "";
        public string CourseCode { get; set; } = "";
        public string CreditValue { get; set; } = "";
        public string HemisResearch1 { get; set; } = "";
        public string HemisQualName { get; set; } = "";
        public string? PqmQualName { get; set; }
        public string? PqmResearch1 { get; set; }
        public bool QualNameMatch { get; set; }
        public bool Research1Match { get; set; }
        public string ValidationResult { get; set; } = "";
        public string ExceptionReason { get; set; } = "";
    }

    public class Rule54ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string CredTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string PqmTable { get; set; } = "";
        public string CredIdCol { get; set; } = "";
        public string CredCourseCol { get; set; } = "";
        public string CredCreditCol { get; set; } = "";
        public string CredResearch1Col { get; set; } = "";
        public string QualIdCol { get; set; } = "";
        public string QualNameCol { get; set; } = "";
        public string PqmNameCol { get; set; } = "";
        public string PqmResearch1Col { get; set; } = "";
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule54ValidationRow> ValidationRows { get; set; } = new();
        public List<Rule54ExceptionRecord> Exceptions { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule54VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string CredTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string PqmTable { get; set; } = "";
        public string CredIdCol { get; set; } = "";
        public string QualIdCol { get; set; } = "";
    }

    public class Rule54VerifyResult
    {
        public bool Success { get; set; }
        public int CredTotal { get; set; }
        public int QualTotal { get; set; }
        public int PqmTotal { get; set; }
        public int MergedTotal { get; set; }
        public string? Error { get; set; }
    }

    public class Rule54TableListResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoCredTable { get; set; }
        public string? AutoQualTable { get; set; }
        public string? AutoPqmTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule54GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
        public string TableRole { get; set; } = "";
    }

    public class Rule54WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string CredTable { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string PqmTable { get; set; } = "";
        public string CredIdCol { get; set; } = "";
        public string CredCourseCol { get; set; } = "";
        public string CredCreditCol { get; set; } = "";
        public string CredResearch1Col { get; set; } = "";
        public string QualIdCol { get; set; } = "";
        public string QualNameCol { get; set; } = "";
        public string PqmNameCol { get; set; } = "";
        public string PqmResearch1Col { get; set; } = "";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule54ValidationSummary? Summary { get; set; }
    }

    public class Rule54WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule54WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule54RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule54ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff =>
            IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload =>
            ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule54RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule54WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }
}
