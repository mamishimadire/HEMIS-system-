using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule24TableDiscoveryResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoQualTable { get; set; }
        public string? AutoAuditTable { get; set; }
        public string? AutoH16Table { get; set; }
        public string? Error { get; set; }
    }

    public class Rule24AuditColumnRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string AuditTable { get; set; } = "";
    }

    public class Rule24AuditColumnResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoQualCodeColumn { get; set; }
        public string? Error { get; set; }
    }

    public class Rule24VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string QualTable { get; set; } = "";
        public string AuditTable { get; set; } = "";
        public string H16Table { get; set; } = "";
        public string QualCodeColumn { get; set; } = "_001";
        public string ApprovalStatusColumn { get; set; } = "_004";
        public string ExcludedApprovalStatusValue { get; set; } = "N";
        public bool Control1OnlyMode { get; set; }
        public string AuditQualCodeColumn { get; set; } = "IAIQUAL";
        public string H16QualCodeColumn { get; set; } = "_001";
    }

    public class Rule24VerifyResult
    {
        public bool Success { get; set; }
        public int QualCount { get; set; }
        public int AuditCount { get; set; }
        public int H16Count { get; set; }
        public string? Error { get; set; }
    }

    public class Rule24ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string QualTable { get; set; } = "";
        public string AuditTable { get; set; } = "";
        public string H16Table { get; set; } = "";
        public string QualCodeColumn { get; set; } = "_001";
        public string ApprovalStatusColumn { get; set; } = "_004";
        public string ExcludedApprovalStatusValue { get; set; } = "N";
        public bool Control1OnlyMode { get; set; }
        public string AuditQualCodeColumn { get; set; } = "IAIQUAL";
        public string H16QualCodeColumn { get; set; } = "_001";
    }

    public class Rule24IssueBreakdownItemViewModel
    {
        public string Status { get; set; } = "";
        public int Count { get; set; }
    }

    public class Rule24ReconciliationRowViewModel
    {
        public int ValidationNumber { get; set; }
        public string QualQualCode { get; set; } = "";
        public string QualApprovalStatus { get; set; } = "";
        public string StudQualCode { get; set; } = "";
        public string AuditQualCode { get; set; } = "";
        public string H16QualCode { get; set; } = "";
        public string ReconciliationStatus { get; set; } = "";
        public string IssueDescription { get; set; } = "";
        public string ControlType { get; set; } = "";
    }

    public class Rule24ValidationSummary
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
        public string QualTable { get; set; } = "";
        public string AuditTable { get; set; } = "";
        public string H16Table { get; set; } = "";
        public string QualCodeColumn { get; set; } = "_001";
        public string ApprovalStatusColumn { get; set; } = "_004";
        public string ExcludedApprovalStatusValue { get; set; } = "N";
        public bool Control1OnlyMode { get; set; }
        public string AuditQualCodeColumn { get; set; } = "IAIQUAL";
        public string H16QualCodeColumn { get; set; } = "_001";
        public int QualCount { get; set; }
        public int AuditCount { get; set; }
        public int H16Count { get; set; }
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public int PassSampleCount { get; set; }
        public bool PassSampleTruncated { get; set; }
        public List<Rule24IssueBreakdownItemViewModel> IssueCounts { get; set; } = new();
        public List<Rule24ReconciliationRowViewModel> PassSampleRows { get; set; } = new();
        public List<Rule24ReconciliationRowViewModel> FailRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule24RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule24ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule24WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string QualTable { get; set; } = "";
        public string AuditTable { get; set; } = "";
        public string H16Table { get; set; } = "";
        public string QualCodeColumn { get; set; } = "_001";
        public string ApprovalStatusColumn { get; set; } = "_004";
        public string ExcludedApprovalStatusValue { get; set; } = "N";
        public bool Control1OnlyMode { get; set; }
        public string AuditQualCodeColumn { get; set; } = "IAIQUAL";
        public string H16QualCodeColumn { get; set; } = "_001";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule24ValidationSummary? Summary { get; set; }
    }

    public class Rule24WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule24WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule24RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule24WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule24SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}


