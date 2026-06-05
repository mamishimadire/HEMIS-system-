using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    // ═══════════════════════════════════════════════════════════════════════════
    // RULE 38 – ENHANCED QUAL vs PQM VALIDATION (5.1.1 – 5.1.6)
    // ═══════════════════════════════════════════════════════════════════════════

    public class Rule38ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";

        // QUAL table
        public string QualTable { get; set; } = "dbo_QUAL";
        public string QualIdCol { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        public string QualApprovalCol { get; set; } = "_004";
        public string QualApprovalValue { get; set; } = "A";
        public string QualTypeCol { get; set; } = "_005";
        public string QualMinTimeTotalCol { get; set; } = "_053";
        public string QualMinTimeWilCol { get; set; } = "_054";
        public string QualHeqfCol { get; set; } = "_084";
        public string QualTotalSubsidyCol { get; set; } = "_090";

        // PQM table
        public string PqmTable { get; set; } = "PQM";
        public string PqmNameCol { get; set; } = "Authorised_Qualification_Name";
        public string PqmQualTypeCol { get; set; } = "HEQF_Qual_Type";
        public string PqmMinTimeTotalCol { get; set; } = "Total2";
        public string PqmWilCol { get; set; } = "WIL_EL2";
        public string PqmAccreditationCol { get; set; } = "CHE_HEQC_Accreditation_Approval_Ref_Nr";
        public string PqmTotalSubsidyCol { get; set; } = "Total2";

        // Control 5 (5.1.5): configurable HEQF/HEQSF indicator prefixes
        // If CHE_HEQC_Accreditation_Approval_Ref_Nr starts with/contains any of these codes,
        // QUAL._084 is expected to be 'Y'; otherwise 'N'.
        public string HeqfIndicatorCodesCsv { get; set; } = "H/,HEQF,HEQSF";

        // M-prefix exclusion: skip qualification codes matching M_____ pattern
        public bool ExcludeMPrefixPattern { get; set; } = false;

        // Postgraduate type codes: comma-separated _005 values that classify a qualification as postgraduate
        public string PostgraduateTypesCsv { get; set; } = "07,27,28,49,72,73,08,30,50,74,75";
    }

    public class Rule38VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string PqmTable { get; set; } = "PQM";
        public string QualApprovalCol { get; set; } = "_004";
        public string QualApprovalValue { get; set; } = "A";
    }

    public class Rule38VerifyResult
    {
        public bool Success { get; set; }
        public int QualTotal { get; set; }
        public int ApprovedCount { get; set; }
        public int PqmTotal { get; set; }
        public string? Error { get; set; }
    }

    public class Rule38TableListResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoQualTable { get; set; }
        public string? AutoPqmTable { get; set; }
        public string? Error { get; set; }
    }

    public class Rule38GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
        public string TableRole { get; set; } = "";  // "qual" | "pqm"
    }

    public class Rule38ValidationRow
    {
        public int ValidationNumber { get; set; }

        // QUAL values
        public string QualCode { get; set; } = "";
        public string QualName { get; set; } = "";
        public string ApprovalStatus { get; set; } = "";
        public string? QualType { get; set; }
        public string? MinTimeTotal { get; set; }
        public string? MinTimeWIL { get; set; }
        public string? HeqfIndicator { get; set; }
        public string? TotalSubsidy { get; set; }

        // PQM values (null when no PQM match)
        public bool HasPqmMatch { get; set; }
        public string? PqmQualType { get; set; }
        public string? PqmMinTimeTotal { get; set; }
        public string? PqmWIL { get; set; }
        public string? PqmAccreditation { get; set; }
        public string? PqmTotalSubsidy { get; set; }

        // Per-control results
        public bool C2_TypeMatch { get; set; }        // 5.1.2
        public bool C3_MinTimeMatch { get; set; }     // 5.1.3
        public bool C4_WILMatch { get; set; }         // 5.1.4
        public bool C5_HeqfMatch { get; set; }        // 5.1.5
        public string? C5_ExpectedHeqf { get; set; }  // expected Y or N
        public bool C6_SubsidyMatch { get; set; }     // 5.1.6

        // Overall
        public string ValidationResult { get; set; } = "";
        public List<string> FailedControls { get; set; } = new();
    }

    public class Rule38ControlSummary
    {
        public string ControlId { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string CriteriaText { get; set; } = "";
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public string Status { get; set; } = "";
    }

    public class Rule38ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalQualRecords { get; set; }
        public int ApprovedCount { get; set; }
        public int PqmMatchCount { get; set; }
        public int PqmNoMatchCount { get; set; }
        public int OverallPassCount { get; set; }
        public int OverallFailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";

        // Config stored with results
        public string QualTable { get; set; } = "dbo_QUAL";
        public string QualIdCol { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        public string QualApprovalCol { get; set; } = "_004";
        public string QualApprovalValue { get; set; } = "A";
        public string QualTypeCol { get; set; } = "_005";
        public string QualMinTimeTotalCol { get; set; } = "_053";
        public string QualMinTimeWilCol { get; set; } = "_054";
        public string QualHeqfCol { get; set; } = "_084";
        public string QualTotalSubsidyCol { get; set; } = "_090";
        public string PqmTable { get; set; } = "PQM";
        public string PqmNameCol { get; set; } = "Authorised_Qualification_Name";
        public string PqmQualTypeCol { get; set; } = "HEQF_Qual_Type";
        public string PqmMinTimeTotalCol { get; set; } = "Total2";
        public string PqmWilCol { get; set; } = "WIL_EL2";
        public string PqmAccreditationCol { get; set; } = "CHE_HEQC_Accreditation_Approval_Ref_Nr";
        public string PqmTotalSubsidyCol { get; set; } = "Total2";
        public string HeqfIndicatorCodesCsv { get; set; } = "H/,HEQF,HEQSF";
        public bool ExcludeMPrefixPattern { get; set; } = false;
        public string PostgraduateTypesCsv { get; set; } = "07,27,28,49,72,73,08,30,50,74,75";

        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule38ControlSummary> ControlSummaries { get; set; } = new();
        public List<Rule38ValidationRow> ValidationRows { get; set; } = new();
        public bool IsPreviewOnly { get; set; }
        public int PreviewLimit { get; set; }
        public string? Error { get; set; }
    }

    public class Rule38WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string QualIdCol { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        public string QualApprovalCol { get; set; } = "_004";
        public string QualApprovalValue { get; set; } = "A";
        public string QualTypeCol { get; set; } = "_005";
        public string QualMinTimeTotalCol { get; set; } = "_053";
        public string QualMinTimeWilCol { get; set; } = "_054";
        public string QualHeqfCol { get; set; } = "_084";
        public string QualTotalSubsidyCol { get; set; } = "_090";
        public string PqmTable { get; set; } = "PQM";
        public string PqmNameCol { get; set; } = "Authorised_Qualification_Name";
        public string PqmQualTypeCol { get; set; } = "HEQF_Qual_Type";
        public string PqmMinTimeTotalCol { get; set; } = "Total2";
        public string PqmWilCol { get; set; } = "WIL_EL2";
        public string PqmAccreditationCol { get; set; } = "CHE_HEQC_Accreditation_Approval_Ref_Nr";
        public string PqmTotalSubsidyCol { get; set; } = "Total2";
        public string HeqfIndicatorCodesCsv { get; set; } = "H/,HEQF,HEQSF";
        public bool ExcludeMPrefixPattern { get; set; } = false;
        public string PostgraduateTypesCsv { get; set; } = "07,27,28,49,72,73,08,30,50,74,75";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public Rule38ValidationSummary? Summary { get; set; }
    }

    public class Rule38WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule38WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule38RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public Rule38ValidationSummary Summary { get; set; } = new();
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

    public class Rule38RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule38WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }
}
