namespace HemisAudit.ViewModels
{
    // ═══════════════════════════════════════════════════════════════════════════
    // RULE 61 – Masters / Doctoral Research Time Validation
    //   Flow   : STUD._010 filter -> STUD._001 -> QUAL._001 -> filter QUAL._005 -> QUAL._003 -> PQM.Authorised_Qualification_Name
    //   Display : STUD._007, STUD._001, STUD._008, STUD._073, STUD._010
    //   Compare: STUD._073 (actual research %) vs PQM.Research_1 (expected %)
    // ═══════════════════════════════════════════════════════════════════════════

    public class Rule61ColumnMapping
    {
        // STUD columns
        public string StudStudentNoCol   { get; set; } = "_007";
        public string StudStatusCol      { get; set; } = "_010";
        public string StudQualCodeCol    { get; set; } = "_001";
        public string StudIdCol          { get; set; } = "_008";
        public string StudResearchTimeCol{ get; set; } = "_073";
        // QUAL columns
        public string QualQualCodeCol    { get; set; } = "_001";
        public string QualNameCol        { get; set; } = "_003";
        public string QualTypeCol        { get; set; } = "_005";
        // PQM columns
        public string PqmNameCol         { get; set; } = "Authorised_Qualification_Name";
        public string PqmResearchTimeCol { get; set; } = "Research_1";
    }

    public class Rule61ValidationRequest
    {
        public int    ClientId   { get; set; }
        public int?   RunId      { get; set; }
        public string Server     { get; set; } = "";
        public string Database   { get; set; } = "";
        public string Driver     { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable  { get; set; } = "dbo_STUD";
        public string QualTable  { get; set; } = "dbo_QUAL";
        public string PqmTable   { get; set; } = "PQM";
        public string StudStatusValue { get; set; } = "N"; // comma-separated STUD._010 values (e.g. N, E)
        public string PgTypesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75";
        public Rule61ColumnMapping ColumnMapping { get; set; } = new();
    }

    public class Rule61ReviewRow
    {
        public int    RowNumber            { get; set; }
        public string StudentNo            { get; set; } = "";
        public string QualCode             { get; set; } = ""; // STUD._001
        public string StudentId            { get; set; } = ""; // STUD._008
        public string StudStatus           { get; set; } = "";
        public string QualJoinCode         { get; set; } = ""; // QUAL._001
        public string QualType             { get; set; } = "";
        public string QualName             { get; set; } = "";
        public string PqmName              { get; set; } = "";
        public string StudResearchTime     { get; set; } = "";
        public string PqmResearchTime      { get; set; } = "";
        public string ValidationResult     { get; set; } = "";
        public string ValidationExplanation{ get; set; } = "";
    }

    public class Rule61ValidationSummary
    {
        public bool    Success         { get; set; }
        public string? Error           { get; set; }
        public string? Warning         { get; set; }
        public int?    SavedRunId      { get; set; }
        public int     ClientId        { get; set; }
        public string  Timestamp       { get; set; } = "";
        public string  Database        { get; set; } = "";
        public string  StudTable       { get; set; } = "dbo_STUD";
        public string  QualTable       { get; set; } = "dbo_QUAL";
        public string  PqmTable        { get; set; } = "PQM";
        public string  StudStatusValue { get; set; } = "N"; // comma-separated STUD._010 values (e.g. N, E)
        public string  PgTypesText     { get; set; } = "";
        public Rule61ColumnMapping ColumnMapping { get; set; } = new();
        public int     TotalCount      { get; set; }
        public int     PassCount       { get; set; }
        public int     FailCount       { get; set; }
        public int     MissingPqmCount { get; set; }
        public decimal ExceptionRate   { get; set; }
        public string  Status          { get; set; } = "";
        public bool    IsPreviewOnly   { get; set; }
        public int     PreviewLimit    { get; set; }
        public List<Rule61ReviewRow> PassRows     { get; set; } = new();
        public List<Rule61ReviewRow> FailRows     { get; set; } = new();
    }

    public class Rule61VerifyRequest
    {
        public string Server    { get; set; } = "";
        public string Database  { get; set; } = "";
        public string Driver    { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "dbo_STUD";
        public string QualTable { get; set; } = "dbo_QUAL";
        public string PqmTable  { get; set; } = "PQM";
        public string StudStatusValue { get; set; } = "N"; // comma-separated STUD._010 values (e.g. N, E)
        public string PgTypesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75";
        public Rule61ColumnMapping ColumnMapping { get; set; } = new();
    }

    public class Rule61VerifyResult
    {
        public bool   Success        { get; set; }
        public int    MastersDoctCount { get; set; }
        public int    PqmCount        { get; set; }
        public string? Error          { get; set; }
    }

    public class Rule61TableDiscoveryResult
    {
        public bool   Success        { get; set; }
        public List<string> Tables   { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoQualTable { get; set; }
        public string? AutoPqmTable  { get; set; }
        public string? Error         { get; set; }
    }

    public class Rule61ColumnDiscoveryResult
    {
        public bool   Success       { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? Error        { get; set; }
    }

    public class Rule61WorkspaceSaveResult
    {
        public bool   Success             { get; set; }
        public string? Error              { get; set; }
        public string? Message            { get; set; }
        public bool   SignoffsCleared     { get; set; }
        public int?   ClearedSignoffCount { get; set; }
        public Rule61WorkspaceStateViewModel? Workspace { get; set; }
    }

    public class Rule61WorkspaceStateViewModel
    {
        public int    ClientId    { get; set; }
        public int?   RunId       { get; set; }
        public bool   ResultsVisible { get; set; }
        public string Server      { get; set; } = "";
        public string Database    { get; set; } = "";
        public string Driver      { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable   { get; set; } = "dbo_STUD";
        public string QualTable   { get; set; } = "dbo_QUAL";
        public string PqmTable    { get; set; } = "PQM";
public string StudStatusValue { get; set; } = "N"; // comma-separated STUD._010 values (e.g. N, E)
        public string PgTypesText { get; set; } = "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75";
        public Rule61ColumnMapping ColumnMapping { get; set; } = new();
        public string CurrentUserEngagementRole  { get; set; } = "";
        public bool   HasDataAnalystSignoff      { get; set; }
        public bool   CurrentUserHasSignedOff    { get; set; }
        public string CurrentUserSignoffComment  { get; set; } = "";
        public string CurrentStatus              { get; set; } = "";
        public string? LastEditedByUserName      { get; set; }
        public DateTime? LastEditedAt            { get; set; }
        public bool   IsWorkspaceSaved           { get; set; }
        public Rule61ValidationSummary? Summary  { get; set; }
    }

    public class Rule61RunReviewViewModel
    {
        public int    RunId          { get; set; }
        public int    ClientId       { get; set; }
        public bool   IsCurrentRun   { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer   { get; set; } = "";
        public string? GeneratedSql  { get; set; }
        public Rule61ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole  { get; set; } = "";
        public bool   HasDataAnalystSignoff      { get; set; }
        public bool   CurrentUserHasSignedOff =>
            Signoffs.Any(s => HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff    => IsCurrentRun && HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload   => HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule61SqlResult
    {
        public bool   Success { get; set; }
        public string? Sql    { get; set; }
        public string? Error  { get; set; }
    }

    public class Rule61RunSignoffInputModel
    {
        public int    RunId   { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule61WorkspaceSignoffInputModel
    {
        public int    ClientId { get; set; }
        public int?   RunId    { get; set; }
        public string Comment  { get; set; } = "";
    }

    public class Rule61GetColumnsRequest
    {
        public string Server    { get; set; } = "";
        public string Database  { get; set; } = "";
        public string Driver    { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
    }
}
