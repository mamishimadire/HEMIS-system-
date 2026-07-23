namespace HemisAudit.ViewModels
{
    public class Rule4001ReconcRow
    {
        public int    RowNumber     { get; set; }
        public string StaffNumber   { get; set; } = "";
        public string OverallResult { get; set; } = "AGREE"; // AGREE | MISSING-H16SFTE | MISSING-VALPAC
    }

    public class Rule4001ValidationRequest
    {
        public int    ClientId      { get; set; }
        public int?   RunId         { get; set; }
        public string Server        { get; set; } = "";
        public string Database      { get; set; } = "";
        public string Driver        { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ValpacTable   { get; set; } = "";
        public string SfteTable     { get; set; } = "";
        public string ValpacKeyCol  { get; set; } = "_037";
        public string SfteKeyCol    { get; set; } = "_037";
    }

    public class Rule4001ValidationSummary
    {
        public bool    Success                { get; set; }
        public string? Error                  { get; set; }
        public int?    SavedRunId             { get; set; }
        public int     ClientId               { get; set; }
        public string  Status                 { get; set; } = "";
        public string  Timestamp              { get; set; } = "";
        public string  Server                 { get; set; } = "";
        public string  Database               { get; set; } = "";
        public string  ValpacTable            { get; set; } = "";
        public string  SfteTable              { get; set; } = "";
        public string  ValpacKeyCol           { get; set; } = "_037";
        public string  SfteKeyCol             { get; set; } = "_037";
        public int     TotalCount             { get; set; }
        public int     AgreeCount             { get; set; }
        public int     MissingInSfteCount     { get; set; }
        public int     MissingInValpacCount   { get; set; }
        public decimal ExceptionRate          { get; set; }
        public List<Rule4001ReconcRow> ReviewRows  { get; set; } = new();
        public List<Rule4001ReconcRow> AgreeSample { get; set; } = new();
    }

    public class Rule4001VerifyRequest
    {
        public string Server      { get; set; } = "";
        public string Database    { get; set; } = "";
        public string Driver      { get; set; } = "ODBC Driver 17 for SQL Server";
        public string ValpacTable { get; set; } = "";
        public string SfteTable   { get; set; } = "";
    }

    public class Rule4001VerifyResult
    {
        public bool   Success    { get; set; }
        public int    ValpacCount { get; set; }
        public int    SfteCount   { get; set; }
        public string? Error      { get; set; }
    }

    public class Rule4001TableDiscoveryResult
    {
        public bool         Success        { get; set; }
        public List<string> Tables         { get; set; } = new();
        public string?      AutoValpacTable { get; set; }
        public string?      AutoSfteTable   { get; set; }
        public string?      Error           { get; set; }
    }

    public class Rule4001WorkspaceState
    {
        public int       ClientId                  { get; set; }
        public int?      RunId                     { get; set; }
        public string    Server                    { get; set; } = "";
        public string    Database                  { get; set; } = "";
        public string    Driver                    { get; set; } = "ODBC Driver 17 for SQL Server";
        public string    ValpacTable               { get; set; } = "";
        public string    SfteTable                 { get; set; } = "";
        public string    ValpacKeyCol              { get; set; } = "_037";
        public string    SfteKeyCol                { get; set; } = "_037";
        public string    CurrentStatus             { get; set; } = "";
        public bool      HasDataAnalystSignoff     { get; set; }
        public bool      CurrentUserHasSignedOff   { get; set; }
        public string    CurrentUserSignoffComment { get; set; } = "";
        public string    CurrentUserEngagementRole { get; set; } = "";
        public bool      IsWorkspaceSaved          { get; set; }
        public bool      ResultsVisible            { get; set; }
        public string?   LastEditedByUserName      { get; set; }
        public DateTime? LastEditedAt              { get; set; }
        public Rule4001ValidationSummary? Summary  { get; set; }
    }

    public class Rule4001SignoffInput
    {
        public int    ClientId { get; set; }
        public int?   RunId    { get; set; }
        public string Comment  { get; set; } = "";
    }
}
