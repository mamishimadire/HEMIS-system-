using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule23TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoAuditTable { get; set; }
        public string? AutoH16Table { get; set; }
        public string? Error { get; set; }
    }

    public class Rule23AuditColumnRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string AuditTable { get; set; } = "";
    }

    public class Rule23AuditColumnResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoStudentNumberColumn { get; set; }
        public string? AutoQualificationColumn { get; set; }
        public string? AutoIdNumberColumn { get; set; }
        public string? Error { get; set; }
    }

    public class Rule23VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string StudTable { get; set; } = "";
        public string AuditTable { get; set; } = "";
        public string H16Table { get; set; } = "";
        public string StudStudentNumberColumn { get; set; } = "_007";
        public string StudQualificationColumn { get; set; } = "_001";
        public string StudIdNumberColumn { get; set; } = "_008";
        public string AuditStudentNumberColumn { get; set; } = "IAGSTNO";
        public string AuditQualificationColumn { get; set; } = "IAGQUAL";
        public string AuditIdNumberColumn { get; set; } = "IADIDNO";
        public string H16StudentNumberColumn { get; set; } = "_007";
        public string H16QualificationColumn { get; set; } = "_001";
        public string H16IdNumberColumn { get; set; } = "_008";
    }

    public class Rule23VerifyResult
    {
        public bool Success { get; set; }
        public int StudCount { get; set; }
        public int AuditCount { get; set; }
        public int H16Count { get; set; }
        public string? Error { get; set; }
    }

    public class Rule23ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "";
        public string AuditTable { get; set; } = "";
        public string H16Table { get; set; } = "";
        public string StudStudentNumberColumn { get; set; } = "_007";
        public string StudQualificationColumn { get; set; } = "_001";
        public string StudIdNumberColumn { get; set; } = "_008";
        public string AuditStudentNumberColumn { get; set; } = "IAGSTNO";
        public string AuditQualificationColumn { get; set; } = "IAGQUAL";
        public string AuditIdNumberColumn { get; set; } = "IADIDNO";
        public string H16StudentNumberColumn { get; set; } = "_007";
        public string H16QualificationColumn { get; set; } = "_001";
        public string H16IdNumberColumn { get; set; } = "_008";
    }

    public class Rule23IssueBreakdownItemViewModel
    {
        public string Status { get; set; } = "";
        public int Count { get; set; }
    }

    public class Rule23ReconciliationRowViewModel
    {
        public int ValidationNumber { get; set; }
        public string StudStudentNumber { get; set; } = "";
        public string StudQualificationCode { get; set; } = "";
        public string StudIdNumber { get; set; } = "";
        public string AuditStudentNumber { get; set; } = "";
        public string AuditQualificationCode { get; set; } = "";
        public string AuditIdNumber { get; set; } = "";
        public string H16StudentNumber { get; set; } = "";
        public string H16QualificationCode { get; set; } = "";
        public string H16IdNumber { get; set; } = "";
        public string ReconciliationStatus { get; set; } = "";
        public string IssueDescription { get; set; } = "";
        public string ResultType =>
            string.Equals(ReconciliationStatus, "MATCH", StringComparison.OrdinalIgnoreCase) ? "PASS" : "FAIL";
        public string ControlType =>
            ReconciliationStatus.IndexOf("MISSING", StringComparison.OrdinalIgnoreCase) >= 0 ? "CONTROL 2" : "CONTROL 1";
    }

    public class Rule23ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int Matches { get; set; }
        public int Mismatches { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public decimal MatchRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string StudTable { get; set; } = "";
        public string AuditTable { get; set; } = "";
        public string H16Table { get; set; } = "";
        public string StudStudentNumberColumn { get; set; } = "_007";
        public string StudQualificationColumn { get; set; } = "_001";
        public string StudIdNumberColumn { get; set; } = "_008";
        public string AuditStudentNumberColumn { get; set; } = "IAGSTNO";
        public string AuditQualificationColumn { get; set; } = "IAGQUAL";
        public string AuditIdNumberColumn { get; set; } = "IADIDNO";
        public string H16StudentNumberColumn { get; set; } = "_007";
        public string H16QualificationColumn { get; set; } = "_001";
        public string H16IdNumberColumn { get; set; } = "_008";
        public int StudCount { get; set; }
        public int AuditCount { get; set; }
        public int H16Count { get; set; }
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public int PassSampleCount { get; set; }
        public bool PassSampleTruncated { get; set; }
        public int SavedFailRowCount { get; set; }
        public bool FailRowsTruncated { get; set; }
        public List<Rule23IssueBreakdownItemViewModel> IssueCounts { get; set; } = new();
        public List<Rule23ReconciliationRowViewModel> PassSampleRows { get; set; } = new();
        public List<Rule23ReconciliationRowViewModel> FailRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule23RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule23ValidationSummary Summary { get; set; } = new();
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

    public class Rule23WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "";
        public string AuditTable { get; set; } = "";
        public string H16Table { get; set; } = "";
        public string StudStudentNumberColumn { get; set; } = "_007";
        public string StudQualificationColumn { get; set; } = "_001";
        public string StudIdNumberColumn { get; set; } = "_008";
        public string AuditStudentNumberColumn { get; set; } = "IAGSTNO";
        public string AuditQualificationColumn { get; set; } = "IAGQUAL";
        public string AuditIdNumberColumn { get; set; } = "IADIDNO";
        public string H16StudentNumberColumn { get; set; } = "_007";
        public string H16QualificationColumn { get; set; } = "_001";
        public string H16IdNumberColumn { get; set; } = "_008";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public Rule23ValidationSummary? Summary { get; set; }
    }

    public class Rule23WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule23WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule23RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule23WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule23SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
