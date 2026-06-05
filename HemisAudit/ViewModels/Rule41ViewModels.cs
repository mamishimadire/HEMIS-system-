namespace HemisAudit.ViewModels
{
    // ═══════════════════════════════════════════════════════════════════════════
    // RULE 41 – Student ASCII Agreement
    //
    // Reconciliation: dbo_STUD  vs  MT-Audit-prod-std
    //   Join key   : _007 (dbo_STUD) ↔ IAGSTNO (MT-Audit-prod-std)
    //   Columns    : _007↔IAGSTNO (Student No), _008↔IADIDNO (Birth Date), _001↔IAGQUAL (Qualification)
    //   Join type  : Full outer join
    // ═══════════════════════════════════════════════════════════════════════════

    public class Rule41ColumnPair
    {
        public string StudCol  { get; set; } = "";
        public string AuditCol { get; set; } = "";
        public string Label    { get; set; } = "";
    }

    public class Rule41FieldValue
    {
        public string StudValue  { get; set; } = "—";
        public string AuditValue { get; set; } = "—";
        public string Match      { get; set; } = "AGREE"; // AGREE | DISAGREE | MISSING
    }

    public class Rule41ReconcRow
    {
        public int    RowNumber      { get; set; }
        public string StudentRef     { get; set; } = "";
        public string OverallResult  { get; set; } = "AGREE";
        public string DisagreeDetail { get; set; } = "";
        public Dictionary<string, Rule41FieldValue> Fields { get; set; } = new();
    }

    public class Rule41ReconciliationSummary
    {
        public string StudTable  { get; set; } = "";
        public string AuditTable { get; set; } = "";
        public string StudKey    { get; set; } = "_007";
        public string AuditKey   { get; set; } = "IAGSTNO";
        public List<Rule41ColumnPair> Pairs { get; set; } = new();
        public int TotalCount    { get; set; }
        public int AgreeCount    { get; set; }
        public int DisagreeCount { get; set; }
        public int MissingCount  { get; set; }
        public decimal ExceptionRate { get; set; }
        public List<Rule41ReconcRow> Rows          { get; set; } = new();
        public List<Rule41ReconcRow> ExceptionRows { get; set; } = new();
    }

    public class Rule41ValidationRequest
    {
        public int    ClientId   { get; set; }
        public int?   RunId      { get; set; }
        public string Server     { get; set; } = "";
        public string Database   { get; set; } = "";
        public string Driver     { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable  { get; set; } = "dbo_STUD";
        public string AuditTable { get; set; } = "MT-Audit-prod-std";
        public string StudKey    { get; set; } = "_007";
        public string AuditKey   { get; set; } = "IAGSTNO";
        public List<Rule41ColumnPair> Pairs { get; set; } = new();
    }

    public class Rule41ValidationSummary
    {
        public bool   Success    { get; set; }
        public string? Error     { get; set; }
        public int?   SavedRunId { get; set; }
        public int    ClientId   { get; set; }
        public string Status     { get; set; } = "";
        public string Timestamp  { get; set; } = "";
        public string Server     { get; set; } = "";
        public string Database   { get; set; } = "";
        public string StudTable  { get; set; } = "dbo_STUD";
        public string AuditTable { get; set; } = "MT-Audit-prod-std";
        public string StudKey    { get; set; } = "_007";
        public string AuditKey   { get; set; } = "IAGSTNO";
        public int TotalCount    { get; set; }
        public int AgreeCount    { get; set; }
        public int DisagreeCount { get; set; }
        public int MissingCount  { get; set; }
        public decimal ExceptionRate { get; set; }
        public Rule41ReconciliationSummary Reconc { get; set; } = new();
    }

    public class Rule41VerifyResult
    {
        public bool   Success    { get; set; }
        public int    StudCount  { get; set; }
        public int    AuditCount { get; set; }
        public string? Error     { get; set; }
    }

    public class Rule41VerifyRequest
    {
        public string Server     { get; set; } = "";
        public string Database   { get; set; } = "";
        public string Driver     { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable  { get; set; } = "dbo_STUD";
        public string AuditTable { get; set; } = "MT-Audit-prod-std";
        public string StudKey    { get; set; } = "_007";
        public string AuditKey   { get; set; } = "IAGSTNO";
    }

    public class Rule41TableDiscoveryResult
    {
        public bool   Success         { get; set; }
        public List<string> Tables    { get; set; } = new();
        public string? AutoStudTable  { get; set; }
        public string? AutoAuditTable { get; set; }
        public string? Error          { get; set; }
    }

    public class Rule41ColumnDiscoveryResult
    {
        public bool   Success        { get; set; }
        public List<string> Columns  { get; set; } = new();
        public string? AutoKey       { get; set; }
        public string? Error         { get; set; }
    }

    public class Rule41WorkspaceSaveResult
    {
        public bool   Success              { get; set; }
        public string? Error               { get; set; }
        public string? Message             { get; set; }
        public bool   SignoffsCleared      { get; set; }
        public int?   ClearedSignoffCount  { get; set; }
        public Rule41WorkspaceStateViewModel? Workspace { get; set; }
    }

    public class Rule41WorkspaceStateViewModel
    {
        public int    ClientId   { get; set; }
        public int?   RunId      { get; set; }
        public string Server     { get; set; } = "";
        public string Database   { get; set; } = "";
        public string Driver     { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable  { get; set; } = "dbo_STUD";
        public string AuditTable { get; set; } = "MT-Audit-prod-std";
        public string StudKey    { get; set; } = "_007";
        public string AuditKey   { get; set; } = "IAGSTNO";
        public string CurrentStatus              { get; set; } = "";
        public bool   HasDataAnalystSignoff      { get; set; }
        public bool   CurrentUserHasSignedOff    { get; set; }
        public string CurrentUserSignoffComment  { get; set; } = "";
        public string CurrentUserEngagementRole  { get; set; } = "";
        public bool   IsWorkspaceSaved           { get; set; }
        public bool   ResultsVisible             { get; set; }
        public string? LastEditedByUserName      { get; set; }
        public DateTime? LastEditedAt            { get; set; }
        public Rule41ValidationSummary? Summary  { get; set; }
    }

    public class Rule41RunReviewViewModel
    {
        public int    RunId         { get; set; }
        public int    ClientId      { get; set; }
        public bool   IsCurrentRun  { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer   { get; set; } = "";
        public string CurrentUserEngagementRole  { get; set; } = "";
        public bool   HasDataAnalystSignoff      { get; set; }
        public bool   CurrentUserHasSignedOff    { get; set; }
        public bool   CanCurrentUserSignOff      { get; set; }
        public bool   ResultsVisible             { get; set; }
        public string? GeneratedSql              { get; set; }
        public Rule41ValidationSummary? Summary  { get; set; }
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
    }

    public class Rule41SqlResult
    {
        public bool   Success { get; set; }
        public string? Sql    { get; set; }
        public string? Error  { get; set; }
    }

    public class Rule41RunSignoffInputModel
    {
        public int    RunId   { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule41WorkspaceSignoffInputModel
    {
        public int    ClientId { get; set; }
        public int?   RunId    { get; set; }
        public string Comment  { get; set; } = "";
    }

    public class Rule41GetColumnsRequest
    {
        public string Server    { get; set; } = "";
        public string Database  { get; set; } = "";
        public string Driver    { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
    }
}
