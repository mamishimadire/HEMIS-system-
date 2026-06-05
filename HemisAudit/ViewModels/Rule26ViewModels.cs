using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    public class Rule26GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
        public bool IsProfTable { get; set; }
    }

    public class Rule26ColumnSelectionResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoPersonnelColumn { get; set; }
        public string? AutoEmploymentTypeColumn { get; set; }
        public string? AutoGenderColumn { get; set; }
        public string? AutoGroupColumn { get; set; }
        public string? AutoBirthDateColumn { get; set; }
        public string? Error { get; set; }
    }

    public class Rule26VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string ProfTable { get; set; } = "";
        public string PayrollTable { get; set; } = "";
        public string ProfPersonnelColumn { get; set; } = "";
        public string PayrollPersonnelColumn { get; set; } = "";
    }

    public class Rule26VerifyResult
    {
        public bool Success { get; set; }
        public int ProfRecordCount { get; set; }
        public int PayrollRecordCount { get; set; }
        public int LinkedRecordCount { get; set; }
        public int ProfWithoutPayrollCount { get; set; }
        public int PayrollWithoutProfCount { get; set; }
        public string? Error { get; set; }
    }

    public class Rule26ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ProfTable { get; set; } = "";
        public string PayrollTable { get; set; } = "";
        public string ProfPersonnelColumn { get; set; } = "";
        public string ProfEmploymentTypeColumn { get; set; } = "";
        public string ProfGenderColumn { get; set; } = "";
        public string ProfGroupColumn { get; set; } = "";
        public string ProfBirthDateColumn { get; set; } = "";
        public string BlankPayrollGroupPassCodes { get; set; } = "Z";
        public string PayrollPersonnelColumn { get; set; } = "";
        public string PayrollEmploymentTypeColumn { get; set; } = "";
        public string PayrollGenderColumn { get; set; } = "";
        public string PayrollGroupColumn { get; set; } = "";
        public string PayrollBirthDateColumn { get; set; } = "";
    }

    public class Rule26ControlSummaryViewModel
    {
        public int ControlNumber { get; set; }
        public string ControlName { get; set; } = "";
        public string Explanation { get; set; } = "";
        public int TotalTested { get; set; }
        public int ExceptionCount { get; set; }
        public bool Passed { get; set; }
    }

    public class Rule26PassRowViewModel
    {
        public string DirectionKey { get; set; } = "";
        public string DirectionLabel { get; set; } = "";
        public string PersonnelNumber { get; set; } = "";
        public string? PersonnelName { get; set; }
        public string? EmploymentType { get; set; }
        public string? Gender { get; set; }
    }

    public class Rule26ExceptionRowViewModel
    {
        public string DirectionKey { get; set; } = "";
        public string DirectionLabel { get; set; } = "";
        public int ControlNumber { get; set; }
        public string ControlName { get; set; } = "";
        public string PersonnelNumber { get; set; } = "";
        public string? PersonnelName { get; set; }
        public string ExceptionReason { get; set; } = "";
        public string BaseValue { get; set; } = "";
        public string ReferenceValue { get; set; } = "";
        public Dictionary<string, string?> DisplayValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule26DirectionResultViewModel
    {
        public string DirectionKey { get; set; } = "";
        public string DirectionLabel { get; set; } = "";
        public string BaseTable { get; set; } = "";
        public string ReferenceTable { get; set; } = "";
        public int BaseRecordCount { get; set; }
        public int LinkedRecordCount { get; set; }
        public int TotalExceptions { get; set; }
        public List<Rule26ControlSummaryViewModel> Controls { get; set; } = new();
        public List<Rule26ExceptionRowViewModel> Exceptions { get; set; } = new();
        public List<Rule26PassRowViewModel> PassRows { get; set; } = new();
    }

    public class Rule26ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int MatchingCount { get; set; }
        public int DisplayedCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string ProfTable { get; set; } = "";
        public string PayrollTable { get; set; } = "";
        public string ProfPersonnelColumn { get; set; } = "";
        public string ProfEmploymentTypeColumn { get; set; } = "";
        public string ProfGenderColumn { get; set; } = "";
        public string ProfGroupColumn { get; set; } = "";
        public string ProfBirthDateColumn { get; set; } = "";
        public string BlankPayrollGroupPassCodes { get; set; } = "Z";
        public string PayrollPersonnelColumn { get; set; } = "";
        public string PayrollEmploymentTypeColumn { get; set; } = "";
        public string PayrollGenderColumn { get; set; } = "";
        public string PayrollGroupColumn { get; set; } = "";
        public string PayrollBirthDateColumn { get; set; } = "";
        public int ProfRecordCount { get; set; }
        public int PayrollRecordCount { get; set; }
        public int LinkedRecordCount { get; set; }
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule26DirectionResultViewModel> Directions { get; set; } = new();
        public List<Rule26ExceptionRowViewModel> Exceptions { get; set; } = new();
        public List<Rule26PassRowViewModel> PassRows { get; set; } = new();
        public string? Warning { get; set; }
        public string? Error { get; set; }
    }

    public class Rule26RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule26ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule26WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ProfTable { get; set; } = "";
        public string PayrollTable { get; set; } = "";
        public string ProfPersonnelColumn { get; set; } = "";
        public string ProfEmploymentTypeColumn { get; set; } = "";
        public string ProfGenderColumn { get; set; } = "";
        public string ProfGroupColumn { get; set; } = "";
        public string ProfBirthDateColumn { get; set; } = "";
        public string BlankPayrollGroupPassCodes { get; set; } = "Z";
        public string PayrollPersonnelColumn { get; set; } = "";
        public string PayrollEmploymentTypeColumn { get; set; } = "";
        public string PayrollGenderColumn { get; set; } = "";
        public string PayrollGroupColumn { get; set; } = "";
        public string PayrollBirthDateColumn { get; set; } = "";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule26ValidationSummary? Summary { get; set; }
    }

    public class Rule26WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule26WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule26RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule26WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule26SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}


