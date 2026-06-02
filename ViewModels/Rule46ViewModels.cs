namespace HemisAudit.ViewModels
{
    public class Rule46ValidationRequest
    {
        public int    ClientId   { get; set; }
        public int?   RunId      { get; set; }
        public string Server     { get; set; } = "";
        public string Database   { get; set; } = "";
        public string Driver     { get; set; } = "ODBC Driver 17 for SQL Server";
        // STUD table
        public string StudTable       { get; set; } = "dbo_STUD";
        public string StudKey         { get; set; } = "_001";
        public string StudFilterCol   { get; set; } = "_106";
        public string StudFilterValue { get; set; } = "Y";
        // QUAL bridge table
        public string QualTable   { get; set; } = "dbo_QUAL";
        public string QualKey     { get; set; } = "_001";
        public string QualNameCol { get; set; } = "_003";
        // CRED table (Control 1)
        public string CredTable    { get; set; } = "dbo_CRED";
        public string CredStudKey  { get; set; } = "_001";
        public string CredCourseCol{ get; set; } = "_030";
        // CRSE table (Control 1)
        public string CrseTable          { get; set; } = "dbo_CRSE";
        public string CrseCourseCol      { get; set; } = "_030";
        public string CrseIndicatorCol   { get; set; } = "_091";
        public string CrseIndicatorValue { get; set; } = "Y";
        // PQM table (both controls)
        public string PqmTable   { get; set; } = "[dbo].[PQM]";
        public string PqmNameCol { get; set; } = "Authorised_Qualification_Name";
    }

    public class Rule46ValidationRow
    {
        public int    RowNumber       { get; set; }
        public string ControlType     { get; set; } = "";
        public string StudId          { get; set; } = "";
        public string StudFilterValue { get; set; } = "";
        public string QualId          { get; set; } = "";
        public string QualName        { get; set; } = "";
        public string CredId          { get; set; } = "";
        public string CredCourse      { get; set; } = "";
        public string PqmName         { get; set; } = "";
        public string ValidationResult{ get; set; } = "PASS";
        public string ResultDetail    { get; set; } = "";
    }

    public class Rule46ControlSummary
    {
        public string ControlType  { get; set; } = "";
        public string ControlLabel { get; set; } = "";
        public string CriteriaText { get; set; } = "";
        public int    TotalCount   { get; set; }
        public int    PassCount    { get; set; }
        public int    FailCount    { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status       { get; set; } = "PASS";
    }

    public class Rule46ValidationSummary
    {
        public bool    Success       { get; set; }
        public string? Error         { get; set; }
        public string  Status        { get; set; } = "";
        public string  Timestamp     { get; set; } = "";
        public string  Database      { get; set; } = "";
        public int     ClientId      { get; set; }
        public int?    SavedRunId    { get; set; }
        // config echo
        public string StudTable       { get; set; } = "dbo_STUD";
        public string StudKey         { get; set; } = "_001";
        public string StudFilterCol   { get; set; } = "_106";
        public string StudFilterValue { get; set; } = "Y";
        public string QualTable       { get; set; } = "dbo_QUAL";
        public string QualKey         { get; set; } = "_001";
        public string QualNameCol     { get; set; } = "_003";
        public string CredTable       { get; set; } = "dbo_CRED";
        public string CredStudKey     { get; set; } = "_001";
        public string CredCourseCol   { get; set; } = "_030";
        public string PqmTable        { get; set; } = "[dbo].[PQM]";
        public string PqmNameCol      { get; set; } = "Authorised_Qualification_Name";
        // totals
        public int TotalValidated { get; set; }
        public int PassCount      { get; set; }
        public int FailCount      { get; set; }
        public decimal ExceptionRate { get; set; }
        // controls
        public List<Rule46ControlSummary> ControlSummaries { get; set; } = new();
        public List<Rule46ValidationRow>  ValidationRows   { get; set; } = new();
    }

    public class Rule46WorkspaceStateViewModel
    {
        public int    ClientId   { get; set; }
        public int?   RunId      { get; set; }
        public string Server     { get; set; } = "";
        public string Database   { get; set; } = "";
        public string Driver     { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable       { get; set; } = "dbo_STUD";
        public string StudKey         { get; set; } = "_001";
        public string StudFilterCol   { get; set; } = "_106";
        public string StudFilterValue { get; set; } = "Y";
        public string QualTable       { get; set; } = "dbo_QUAL";
        public string QualKey         { get; set; } = "_001";
        public string QualNameCol     { get; set; } = "_003";
        public string CredTable       { get; set; } = "dbo_CRED";
        public string CredStudKey     { get; set; } = "_001";
        public string CredCourseCol   { get; set; } = "_030";
        public string PqmTable        { get; set; } = "[dbo].[PQM]";
        public string PqmNameCol      { get; set; } = "Authorised_Qualification_Name";
        public string CurrentStatus              { get; set; } = "";
        public bool   HasDataAnalystSignoff      { get; set; }
        public bool   CurrentUserHasSignedOff    { get; set; }
        public string CurrentUserSignoffComment  { get; set; } = "";
        public string CurrentUserEngagementRole  { get; set; } = "";
        public bool   IsWorkspaceSaved           { get; set; }
        public bool   ResultsVisible             { get; set; }
        public string? LastEditedByUserName      { get; set; }
        public DateTime? LastEditedAt            { get; set; }
        public Rule46ValidationSummary? Summary  { get; set; }
    }

    public class Rule46WorkspaceSaveResult
    {
        public bool   Success             { get; set; }
        public string? Error              { get; set; }
        public string? Message            { get; set; }
        public bool   SignoffsCleared     { get; set; }
        public int?   ClearedSignoffCount { get; set; }
        public Rule46WorkspaceStateViewModel? Workspace { get; set; }
    }

    public class Rule46RunReviewViewModel
    {
        public int    RunId          { get; set; }
        public int    ClientId       { get; set; }
        public bool   IsCurrentRun   { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer   { get; set; } = "";
        public string CurrentUserEngagementRole  { get; set; } = "";
        public bool   HasDataAnalystSignoff      { get; set; }
        public bool   CurrentUserHasSignedOff    { get; set; }
        public bool   CanCurrentUserSignOff      { get; set; }
        public string? GeneratedSql              { get; set; }
        public Rule46ValidationSummary? Summary  { get; set; }
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
    }

    public class Rule46WorkspaceSignoffInputModel
    {
        public int    ClientId { get; set; }
        public int?   RunId    { get; set; }
        public string Comment  { get; set; } = "";
    }

    public class Rule46RunSignoffInputModel
    {
        public int    RunId   { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule46TableDiscoveryResult
    {
        public bool         Success        { get; set; }
        public List<string> Tables         { get; set; } = new();
        public string?      AutoStudTable  { get; set; }
        public string?      AutoQualTable  { get; set; }
        public string?      AutoCredTable  { get; set; }
        public string?      AutoPqmTable   { get; set; }
        public string?      Error          { get; set; }
    }

    public class Rule46ColumnDiscoveryResult
    {
        public bool         Success  { get; set; }
        public List<string> Columns  { get; set; } = new();
        public string?      Error    { get; set; }
    }

    public class Rule46VerifyResult
    {
        public bool   Success    { get; set; }
        public int    StudCount  { get; set; }
        public int    QualCount  { get; set; }
        public int    CredCount  { get; set; }
        public int    PqmCount   { get; set; }
        public string? Error     { get; set; }
    }

    public class Rule46SqlResult
    {
        public bool   Success { get; set; }
        public string Sql     { get; set; } = "";
        public string? Error  { get; set; }
    }
}
