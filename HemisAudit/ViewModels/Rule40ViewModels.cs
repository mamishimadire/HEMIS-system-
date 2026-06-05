using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    // ═══════════════════════════════════════════════════════════════════════════
    // RULE 40 – PROF ASCII Staff Agreement  (Procedure 4.5.2)
    //
    // Reconciliation A: dbo_PROF  vs  2025H16PROF  (VALPAC)
    //   Join key: _037  |  Test: common columns (_037 _038 _039 _040 _011 _012 _013 _014 _042 _046 _047 _048)
    //   Scope: unique _037 values from 2025H16SFTE
    //
    // Reconciliation B: dbo_PROF  vs  2025H16SFTE
    //   Join key: _037  |  Test: _037 only (full outer join)
    // ═══════════════════════════════════════════════════════════════════════════

    public class Rule40ColumnPair
    {
        public string ProfCol  { get; set; } = "";
        public string OtherCol { get; set; } = "";
        public string Label    { get; set; } = "";
    }

    public class Rule40FieldValue
    {
        public string ProfValue  { get; set; } = "—";
        public string OtherValue { get; set; } = "—";
        public string Match      { get; set; } = "AGREE"; // AGREE | DISAGREE | MISSING
    }

    public class Rule40ReconcRow
    {
        public int    RowNumber      { get; set; }
        public string StaffRef       { get; set; } = "";
        public string OverallResult  { get; set; } = "AGREE";
        public string DisagreeDetail { get; set; } = "";
        public Dictionary<string, Rule40FieldValue> Fields { get; set; } = new();
    }

    public class Rule40ReconciliationSummary
    {
        public string ProfTable  { get; set; } = "";
        public string OtherTable { get; set; } = "";
        public string ProfKey    { get; set; } = "_037";
        public string OtherKey   { get; set; } = "_037";
        public List<Rule40ColumnPair> Pairs { get; set; } = new();
        public int TotalCount    { get; set; }
        public int AgreeCount    { get; set; }
        public int DisagreeCount { get; set; }
        public int MissingCount  { get; set; }
        public decimal ExceptionRate { get; set; }
        public List<Rule40ReconcRow> Rows          { get; set; } = new();
        public List<Rule40ReconcRow> ExceptionRows { get; set; } = new();
    }

    public class Rule40ValidationRequest
    {
        public int    ClientId  { get; set; }
        public int?   RunId     { get; set; }
        public string Server    { get; set; } = "";
        public string Database  { get; set; } = "";
        public string Driver    { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ProfTable   { get; set; } = "dbo_PROF";
        public string ValpacTable { get; set; } = "2025H16PROF";
        public string SfteTable   { get; set; } = "2025H16SFTE";
        public string ProfKey   { get; set; } = "_037";
        public string ValpacKey { get; set; } = "_037";
        public string SfteKey   { get; set; } = "_037";
        public List<Rule40ColumnPair> ValpacPairs { get; set; } = new();
        public List<Rule40ColumnPair> SftePairs   { get; set; } = new();
    }

    public class Rule40ValidationSummary
    {
        public bool   Success    { get; set; }
        public string? Error     { get; set; }
        public int?   SavedRunId { get; set; }
        public int    ClientId   { get; set; }
        public string Status     { get; set; } = "";
        public string Timestamp  { get; set; } = "";
        public string Server     { get; set; } = "";
        public string Database   { get; set; } = "";
        public string ProfTable  { get; set; } = "dbo_PROF";
        public string ValpacTable{ get; set; } = "2025H16PROF";
        public string SfteTable  { get; set; } = "2025H16SFTE";
        public string ProfKey    { get; set; } = "_037";
        public string ValpacKey  { get; set; } = "_037";
        public string SfteKey    { get; set; } = "_037";
        // Overall counts (based on Reconciliation A scope)
        public int TotalCount    { get; set; }
        public int AgreeCount    { get; set; }
        public int DisagreeCount { get; set; }
        public int MissingCount  { get; set; }
        public decimal ExceptionRate { get; set; }
        public Rule40ReconciliationSummary ReconcA { get; set; } = new(); // PROF vs VALPAC
        public Rule40ReconciliationSummary ReconcB { get; set; } = new(); // PROF vs SFTE
    }

    public class Rule40VerifyResult
    {
        public bool   Success     { get; set; }
        public int    ProfCount   { get; set; }
        public int    ValpacCount { get; set; }
        public int    SfteCount   { get; set; }
        public int    ScopeCount  { get; set; }
        public string? Error      { get; set; }
    }

    public class Rule40VerifyRequest
    {
        public string Server     { get; set; } = "";
        public string Database   { get; set; } = "";
        public string Driver     { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ProfTable  { get; set; } = "dbo_PROF";
        public string ValpacTable{ get; set; } = "2025H16PROF";
        public string SfteTable  { get; set; } = "2025H16SFTE";
        public string ProfKey    { get; set; } = "_037";
        public string SfteKey    { get; set; } = "_037";
    }

    public class Rule40TableDiscoveryResult
    {
        public bool   Success      { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoProfTable  { get; set; }
        public string? AutoValpacTable{ get; set; }
        public string? AutoSfteTable  { get; set; }
        public string? Error          { get; set; }
    }

    public class Rule40ColumnDiscoveryResult
    {
        public bool   Success        { get; set; }
        public List<string> Columns  { get; set; } = new();
        public string? AutoKey       { get; set; }
        public string? Error         { get; set; }
    }

    public class Rule40WorkspaceSaveResult
    {
        public bool   Success              { get; set; }
        public string? Error               { get; set; }
        public string? Message             { get; set; }
        public bool   SignoffsCleared      { get; set; }
        public int?   ClearedSignoffCount  { get; set; }
        public Rule40WorkspaceStateViewModel? Workspace { get; set; }
    }

    public class Rule40WorkspaceStateViewModel
    {
        public int    ClientId   { get; set; }
        public int?   RunId      { get; set; }
        public string Server     { get; set; } = "";
        public string Database   { get; set; } = "";
        public string Driver     { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ProfTable  { get; set; } = "dbo_PROF";
        public string ValpacTable{ get; set; } = "2025H16PROF";
        public string SfteTable  { get; set; } = "2025H16SFTE";
        public string ProfKey    { get; set; } = "_037";
        public string ValpacKey  { get; set; } = "_037";
        public string SfteKey    { get; set; } = "_037";
        public string CurrentStatus              { get; set; } = "";
        public bool   HasDataAnalystSignoff      { get; set; }
        public bool   CurrentUserHasSignedOff    { get; set; }
        public string CurrentUserSignoffComment  { get; set; } = "";
        public string CurrentUserEngagementRole  { get; set; } = "";
        public bool   IsWorkspaceSaved           { get; set; }
        public bool   ResultsVisible             { get; set; }
        public string? LastEditedByUserName      { get; set; }
        public DateTime? LastEditedAt            { get; set; }
        public Rule40ValidationSummary? Summary  { get; set; }
    }

    public class Rule40RunReviewViewModel
    {
        public int    RunId         { get; set; }
        public int    ClientId      { get; set; }
        public bool   IsCurrentRun  { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer   { get; set; } = "";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool   HasDataAnalystSignoff      { get; set; }
        public bool   CurrentUserHasSignedOff    { get; set; }
        public bool   CanCurrentUserSignOff      { get; set; }
        public bool   ResultsVisible             { get; set; }
        public string? GeneratedSql              { get; set; }
        public Rule40ValidationSummary? Summary  { get; set; }
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
    }

    public class Rule40SqlResult
    {
        public bool   Success { get; set; }
        public string? Sql    { get; set; }
        public string? Error  { get; set; }
    }

    public class Rule40RunSignoffInputModel
    {
        public int    RunId   { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule40WorkspaceSignoffInputModel
    {
        public int    ClientId { get; set; }
        public int?   RunId    { get; set; }
        public string Comment  { get; set; } = "";
    }

    public class Rule40GetColumnsRequest
    {
        public string Server    { get; set; } = "";
        public string Database  { get; set; } = "";
        public string Driver    { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
    }
}
