using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using HemisAudit.Helpers;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule46Service : IRule46Service
    {
        private const int BrowserPreviewRowLimit = 50;
        private readonly IConfiguration _configuration;

        public Rule46Service(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // ── Connection helpers ─────────────────────────────────────────────────

        private static string BuildConnectionString(string server, string database, string driver)
        {
            if (server.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase))
            {
                var pipe = ResolveLocalDbPipe(server);
                if (pipe != null)
                    return $"Server={pipe};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=30;";
            }
            return $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";
        }

        private static string? ResolveLocalDbPipe(string server)
        {
            try
            {
                var instance = server.Contains('\\') ? server.Split('\\').Last().Trim() : "MSSQLLocalDB";
                using (var startP = Process.Start(new ProcessStartInfo("sqllocaldb", $"start \"{instance}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!)
                { startP.WaitForExit(8000); }
                using var infoP = Process.Start(new ProcessStartInfo("sqllocaldb", $"info \"{instance}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
                var output = infoP.StandardOutput.ReadToEnd();
                infoP.WaitForExit(3000);
                var m = System.Text.RegularExpressions.Regex.Match(output, @"Instance pipe name:\s*(np:[^\r\n]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }
            catch { return null; }
        }

        private async Task<SqlConnection> OpenSystemConnectionAsync()
        {
            var server   = _configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = _configuration["SystemDatabase:Name"]   ?? "HEMISBaseSystem";
            var trust    = _configuration.GetValue("SystemDatabase:TrustServerCertificate", true);
            var builder  = new SqlConnectionStringBuilder
            {
                DataSource             = server,
                InitialCatalog         = database,
                IntegratedSecurity     = true,
                TrustServerCertificate = trust,
                Encrypt                = false,
                ConnectTimeout         = 180
            };
            var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            return conn;
        }

        // ── Discovery ──────────────────────────────────────────────────────────

        public async Task<DatabaseListResult> GetDatabasesAsync(string server, string driver)
        {
            try
            {
                await using var conn = new SqlConnection(BuildConnectionString(server, "master", driver));
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT name FROM sys.databases WHERE name NOT IN ('master','tempdb','model','msdb') ORDER BY name;";
                await using var reader = await cmd.ExecuteReaderAsync();
                var items = new List<string>();
                while (await reader.ReadAsync()) items.Add(reader.GetString(0));
                return new DatabaseListResult { Success = true, Databases = items };
            }
            catch (Exception ex) { return new DatabaseListResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule46TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
        {
            try
            {
                await using var conn = new SqlConnection(BuildConnectionString(server, database, driver));
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT TABLE_SCHEMA + '.' + TABLE_NAME AS FullName, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME;";
                await using var reader = await cmd.ExecuteReaderAsync();
                var tables = new List<string>();
                while (await reader.ReadAsync()) tables.Add(reader.GetString(1));
                return new Rule46TableDiscoveryResult
                {
                    Success       = true,
                    Tables        = tables,
                    AutoStudTable = FindFirst(tables, ["dbo_STUD", "STUD"],         ["stud"]),
                    AutoQualTable = FindFirst(tables, ["dbo_QUAL", "QUAL"],         ["qual"]),
                    AutoCredTable = FindFirst(tables, ["dbo_CRED", "CRED"],         ["cred"]),
                    AutoPqmTable  = FindFirst(tables, ["PQM"],                      ["pqm", "PQM"])
                };
            }
            catch (Exception ex) { return new Rule46TableDiscoveryResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule46ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                ValidateObjectName(tableName);
                var tbl = Sanitise(tableName);
                await using var conn = new SqlConnection(BuildConnectionString(server, database, driver));
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @T ORDER BY ORDINAL_POSITION;";
                cmd.Parameters.AddWithValue("@T", tbl);
                await using var reader = await cmd.ExecuteReaderAsync();
                var cols = new List<string>();
                while (await reader.ReadAsync()) if (!reader.IsDBNull(0)) cols.Add(reader.GetString(0));
                return new Rule46ColumnDiscoveryResult { Success = true, Columns = cols };
            }
            catch (Exception ex) { return new Rule46ColumnDiscoveryResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule46VerifyResult> VerifyTablesAsync(Rule46ValidationRequest req)
        {
            try
            {
                ValidateRequest(req);
                await using var conn = new SqlConnection(BuildConnectionString(req.Server, req.Database, req.Driver));
                await conn.OpenAsync();
                return new Rule46VerifyResult
                {
                    Success   = true,
                    StudCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(req.StudTable)}];"),
                    QualCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(req.QualTable)}];"),
                    CredCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(req.CredTable)}];"),
                    PqmCount  = await CountAsync(conn, $"SELECT COUNT(*) FROM {SanitisePqm(req.PqmTable)};")
                };
            }
            catch (Exception ex) { return new Rule46VerifyResult { Success = false, Error = ex.Message }; }
        }

        // ── Validation ─────────────────────────────────────────────────────────

        public async Task<Rule46ValidationSummary> RunValidationAsync(Rule46ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                var summary = await AnalyseAsync(request);

                if (summary.Success && request.ClientId > 0)
                {
                    try { summary.SavedRunId = await SaveValidationRunAsync(request, summary, userEmail, userName); }
                    catch (Exception ex)
                    {
                        summary.Success = false;
                        summary.Error   = $"Analysis completed but could not be saved: {ex.Message}";
                        return summary;
                    }
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex) { return new Rule46ValidationSummary { Success = false, Error = ex.Message }; }
        }

        private static void ApplyBrowserPreview(Rule46ValidationSummary summary)
        {
            if (summary.ValidationRows.Count > BrowserPreviewRowLimit * 2)
                summary.ValidationRows = summary.ValidationRows.Take(BrowserPreviewRowLimit * 2).ToList();
        }

        private async Task<Rule46ValidationSummary> AnalyseAsync(Rule46ValidationRequest req)
        {
            await using var conn = new SqlConnection(BuildConnectionString(req.Server, req.Database, req.Driver));
            await conn.OpenAsync();

            var c1Rows = await RunControlAsync(conn, req, "Control_1");
            var c2Rows = await RunControlAsync(conn, req, "Control_2");

            var allRows  = c1Rows.Concat(c2Rows).ToList();
            var total    = allRows.Count;
            var passed   = allRows.Count(r => r.ValidationResult == "PASS");
            var failed   = allRows.Count(r => r.ValidationResult == "FAIL");
            var rate     = total > 0 ? Math.Round((decimal)failed / total * 100, 2) : 0m;
            var overallStatus = failed == 0 ? "PASS" : "FAIL";

            var c1Pass = c1Rows.Count(r => r.ValidationResult == "PASS");
            var c1Fail = c1Rows.Count(r => r.ValidationResult == "FAIL");
            var c2Pass = c2Rows.Count(r => r.ValidationResult == "PASS");
            var c2Fail = c2Rows.Count(r => r.ValidationResult == "FAIL");

            return new Rule46ValidationSummary
            {
                Success       = true,
                Status        = overallStatus,
                Timestamp     = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database      = req.Database,
                ClientId      = req.ClientId,
                StudTable     = req.StudTable,    StudKey       = req.StudKey,
                StudFilterCol = req.StudFilterCol, StudFilterValue = req.StudFilterValue,
                QualTable     = req.QualTable,    QualKey       = req.QualKey,    QualNameCol = req.QualNameCol,
                CredTable     = req.CredTable,    CredStudKey   = req.CredStudKey, CredCourseCol = req.CredCourseCol,
                PqmTable      = req.PqmTable,     PqmNameCol    = req.PqmNameCol,
                TotalValidated = total,
                PassCount     = passed,
                FailCount     = failed,
                ExceptionRate = rate,
                ControlSummaries = new List<Rule46ControlSummary>
                {
                    new()
                    {
                        ControlType   = "Control_1",
                        ControlLabel  = $"Control 1: Foundation students linked to CRED ({req.StudTable}.{req.StudFilterCol}='{req.StudFilterValue}')",
                        CriteriaText  = $"SELECT {req.StudTable}.{req.StudKey} → {req.QualTable}.{req.QualKey} → {req.CredTable}.{req.CredStudKey}  WHERE {req.StudTable}.{req.StudFilterCol} = '{req.StudFilterValue}'",
                        TotalCount    = c1Rows.Count,
                        PassCount     = c1Pass,
                        FailCount     = c1Fail,
                        ExceptionRate = c1Rows.Count > 0 ? Math.Round((decimal)c1Fail / c1Rows.Count * 100, 2) : 0m,
                        Status        = c1Fail == 0 ? "PASS" : "FAIL"
                    },
                    new()
                    {
                        ControlType   = "Control_2",
                        ControlLabel  = $"Control 2: Foundation students qualification in PQM ({req.QualTable}.{req.QualNameCol} = {req.PqmTable}.{req.PqmNameCol})",
                        CriteriaText  = $"SELECT {req.StudTable}.{req.StudKey} → {req.QualTable}.{req.QualKey} → {req.PqmTable}.{req.PqmNameCol}  WHERE {req.StudTable}.{req.StudFilterCol} = '{req.StudFilterValue}'",
                        TotalCount    = c2Rows.Count,
                        PassCount     = c2Pass,
                        FailCount     = c2Fail,
                        ExceptionRate = c2Rows.Count > 0 ? Math.Round((decimal)c2Fail / c2Rows.Count * 100, 2) : 0m,
                        Status        = c2Fail == 0 ? "PASS" : "FAIL"
                    }
                },
                ValidationRows = allRows
            };
        }

        private async Task<List<Rule46ValidationRow>> RunControlAsync(SqlConnection conn, Rule46ValidationRequest req, string controlType)
        {
            var st  = Sanitise(req.StudTable);
            var sk  = Sanitise(req.StudKey);
            var sfc = Sanitise(req.StudFilterCol);
            var sfv = (req.StudFilterValue ?? "Y").Replace("'", "''");
            var qt  = Sanitise(req.QualTable);
            var qk  = Sanitise(req.QualKey);
            var qn  = Sanitise(req.QualNameCol);

            string sql;
            if (controlType == "Control_1")
            {
                var ct  = Sanitise(req.CredTable);
                var csk = Sanitise(req.CredStudKey);
                var cc  = Sanitise(req.CredCourseCol);
                sql = $@"
SELECT
    CONVERT(NVARCHAR(255), s.[{sk}])  AS STUD__001,
    CONVERT(NVARCHAR(50),  s.[{sfc}]) AS STUD__106,
    CONVERT(NVARCHAR(255), q.[{qk}])  AS QUAL__001,
    CONVERT(NVARCHAR(500), q.[{qn}])  AS QUAL__003,
    CONVERT(NVARCHAR(255), c.[{csk}]) AS CRED__001,
    CONVERT(NVARCHAR(255), c.[{cc}])  AS CRED__030,
    CASE WHEN c.[{csk}] IS NOT NULL THEN 'PASS' ELSE 'FAIL' END AS Validation_Result,
    CASE WHEN c.[{csk}] IS NOT NULL
         THEN 'PASS: Foundation student has a CRED record (' + LTRIM(RTRIM(CONVERT(NVARCHAR(255), s.[{sk}]))) + ')'
         ELSE 'FAIL: No matching CRED record found for student ' + LTRIM(RTRIM(CONVERT(NVARCHAR(255), s.[{sk}])))
    END AS Result_Detail
FROM [{st}] s
LEFT JOIN [{qt}] q ON UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), q.[{qk}])))) = UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), s.[{sk}]))))
LEFT JOIN [{ct}] c ON UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), c.[{csk}])))) = UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), s.[{sk}]))))
WHERE UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(50), s.[{sfc}])))) = '{sfv.ToUpperInvariant()}'
ORDER BY s.[{sk}];";
            }
            else
            {
                var pqm = SanitisePqm(req.PqmTable);
                var pn  = Sanitise(req.PqmNameCol);
                sql = $@"
SELECT
    CONVERT(NVARCHAR(255), s.[{sk}])  AS STUD__001,
    CONVERT(NVARCHAR(50),  s.[{sfc}]) AS STUD__106,
    CONVERT(NVARCHAR(255), q.[{qk}])  AS QUAL__001,
    CONVERT(NVARCHAR(500), q.[{qn}])  AS QUAL__003,
    CONVERT(NVARCHAR(500), p.[{pn}])  AS PQM__NAME,
    CASE WHEN p.[{pn}] IS NOT NULL THEN 'PASS' ELSE 'FAIL' END AS Validation_Result,
    CASE WHEN p.[{pn}] IS NOT NULL
         THEN 'PASS: Qualification ''' + LTRIM(RTRIM(CONVERT(NVARCHAR(500), q.[{qn}]))) + ''' found in PQM register'
         ELSE 'FAIL: Qualification ''' + ISNULL(LTRIM(RTRIM(CONVERT(NVARCHAR(500), q.[{qn}]))), '(null)') + ''' not found in PQM register'
    END AS Result_Detail
FROM [{st}] s
LEFT JOIN [{qt}] q ON UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), q.[{qk}])))) = UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(255), s.[{sk}]))))
LEFT JOIN {pqm} p ON UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(500), p.[{pn}])))) = UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(500), q.[{qn}]))))
WHERE UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(50), s.[{sfc}])))) = '{sfv.ToUpperInvariant()}'
ORDER BY s.[{sk}];";
            }

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandTimeout = SqlLargeDataExtensions.LargeDataCommandTimeoutSeconds;
            cmd.CommandText    = sql;
            await using var reader = await cmd.ExecuteReaderAsync();

            var rows = new List<Rule46ValidationRow>();
            int rowNo = 0;
            while (await reader.ReadAsync())
            {
                rowNo++;
                string Read(string col)
                {
                    try { var idx = reader.GetOrdinal(col); return reader.IsDBNull(idx) ? "" : reader.GetString(idx).Trim(); }
                    catch { return ""; }
                }

                var row = new Rule46ValidationRow
                {
                    RowNumber        = rowNo,
                    ControlType      = controlType,
                    StudId           = Read("STUD__001"),
                    StudFilterValue  = Read("STUD__106"),
                    QualId           = Read("QUAL__001"),
                    QualName         = Read("QUAL__003"),
                    ValidationResult = Read("Validation_Result"),
                    ResultDetail     = Read("Result_Detail")
                };
                if (controlType == "Control_1")
                {
                    row.CredId     = Read("CRED__001");
                    row.CredCourse = Read("CRED__030");
                }
                else
                {
                    row.PqmName = Read("PQM__NAME");
                }
                rows.Add(row);
            }
            return rows;
        }

        // ── SQL generation ─────────────────────────────────────────────────────

        public string GenerateControl1Sql(Rule46ValidationRequest req)
        {
            var st  = Sanitise(req.StudTable);  var sk  = Sanitise(req.StudKey);
            var sfc = Sanitise(req.StudFilterCol); var sfv = (req.StudFilterValue ?? "Y").Replace("'","''");
            var qt  = Sanitise(req.QualTable);  var qk  = Sanitise(req.QualKey); var qn = Sanitise(req.QualNameCol);
            var ct  = Sanitise(req.CredTable);  var csk = Sanitise(req.CredStudKey); var cc = Sanitise(req.CredCourseCol);
            return $@"-- RULE 46 CONTROL 1: Foundation Students → QUAL → CRED
-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
-- Filter: [{st}].[{sfc}] = '{sfv}'
SELECT
    s.[{sk}]  AS [{st}_{sk}],
    s.[{sfc}] AS [{st}_{sfc}],
    q.[{qk}]  AS [{qt}_{qk}],
    q.[{qn}]  AS [{qt}_{qn}],
    c.[{csk}] AS [{ct}_{csk}],
    c.[{cc}]  AS [{ct}_{cc}],
    CASE WHEN c.[{csk}] IS NOT NULL THEN 'PASS' ELSE 'FAIL' END AS Result
FROM [{st}] s
LEFT JOIN [{qt}] q ON UPPER(LTRIM(RTRIM(CAST(q.[{qk}] AS NVARCHAR(255))))) = UPPER(LTRIM(RTRIM(CAST(s.[{sk}] AS NVARCHAR(255)))))
LEFT JOIN [{ct}] c ON UPPER(LTRIM(RTRIM(CAST(c.[{csk}] AS NVARCHAR(255))))) = UPPER(LTRIM(RTRIM(CAST(s.[{sk}] AS NVARCHAR(255)))))
WHERE UPPER(LTRIM(RTRIM(CAST(s.[{sfc}] AS NVARCHAR(50))))) = '{sfv.ToUpperInvariant()}'
ORDER BY s.[{sk}];".Trim();
        }

        public string GenerateControl2Sql(Rule46ValidationRequest req)
        {
            var st  = Sanitise(req.StudTable);  var sk  = Sanitise(req.StudKey);
            var sfc = Sanitise(req.StudFilterCol); var sfv = (req.StudFilterValue ?? "Y").Replace("'","''");
            var qt  = Sanitise(req.QualTable);  var qk  = Sanitise(req.QualKey); var qn = Sanitise(req.QualNameCol);
            var pqm = SanitisePqm(req.PqmTable); var pn  = Sanitise(req.PqmNameCol);
            return $@"-- RULE 46 CONTROL 2: Foundation Students → QUAL → PQM
-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
-- Filter: [{st}].[{sfc}] = '{sfv}'
SELECT
    s.[{sk}]  AS [{st}_{sk}],
    s.[{sfc}] AS [{st}_{sfc}],
    q.[{qk}]  AS [{qt}_{qk}],
    q.[{qn}]  AS [{qt}_{qn}],
    p.[{pn}]  AS [PQM_{pn}],
    CASE WHEN p.[{pn}] IS NOT NULL THEN 'PASS' ELSE 'FAIL' END AS Result
FROM [{st}] s
LEFT JOIN [{qt}] q ON UPPER(LTRIM(RTRIM(CAST(q.[{qk}] AS NVARCHAR(255))))) = UPPER(LTRIM(RTRIM(CAST(s.[{sk}] AS NVARCHAR(255)))))
LEFT JOIN {pqm} p ON UPPER(LTRIM(RTRIM(CAST(p.[{pn}] AS NVARCHAR(500))))) = UPPER(LTRIM(RTRIM(CAST(q.[{qn}] AS NVARCHAR(500)))))
WHERE UPPER(LTRIM(RTRIM(CAST(s.[{sfc}] AS NVARCHAR(50))))) = '{sfv.ToUpperInvariant()}'
ORDER BY s.[{sk}];".Trim();
        }

        // ── Save / Load ────────────────────────────────────────────────────────

        private async Task<int> SaveValidationRunAsync(Rule46ValidationRequest request, Rule46ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 46);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 46);
            var json = ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary));

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
INSERT INTO dbo.ValidationRuns
(
    ClientID, UserID, RuleNumber, RuleName, Status, TotalRecords, PassCount, FailCount, ExceptionRate, RunTimestamp,
    HemisServer, AuditDatabase, StudTable, DeceasedTable, StudColumn, DeceasedColumn,
    ExceptionsJSON, ResultsJSON, RunByUserName, LastEditedByUserName, LastEditedAt, PreviousHash, RecordHash, IsCurrent
)
OUTPUT INSERTED.RunID
VALUES
(
    @ClientID, @UserID, 46, 'Foundation Student PQM Validation', @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @QualTable, @CredTable, @PqmTable,
    NULL, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);";
            command.Parameters.AddWithValue("@ClientID",     request.ClientId);
            command.Parameters.AddWithValue("@UserID",       systemUserId.Value);
            command.Parameters.AddWithValue("@Status",       summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount",    summary.PassCount);
            command.Parameters.AddWithValue("@FailCount",    summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate",summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer",  request.Server);
            command.Parameters.AddWithValue("@AuditDatabase",request.Database);
            command.Parameters.AddWithValue("@StudTable",    request.StudTable);
            command.Parameters.AddWithValue("@QualTable",    request.QualTable);
            command.Parameters.AddWithValue("@CredTable",    request.CredTable);
            command.Parameters.AddWithValue("@PqmTable",     request.PqmTable);
            command.Parameters.AddWithValue("@ResultsJSON",  json);
            command.Parameters.AddWithValue("@RunByUserName",(object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCmd = connection.CreateConfiguredCommand();
            hashCmd.CommandText = "UPDATE dbo.ValidationRuns SET RecordHash = @RecordHash WHERE RunID = @RunID;";
            hashCmd.Parameters.AddWithValue("@RunID",      runId);
            hashCmd.Parameters.AddWithValue("@RecordHash", ComputeHash($"Rule46|{runId}|{request.ClientId}|{summary.Status}|{summary.TotalValidated}|{summary.Timestamp}|{previousHash}"));
            await hashCmd.ExecuteNonQueryAsync();

            return runId;
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        public async Task<Rule46WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID, vr.ClientID,
    ISNULL(vr.HemisServer,'')      AS Server,
    ISNULL(vr.AuditDatabase,'')    AS [Database],
    ISNULL(vr.StudTable,'dbo_STUD') AS StudTable,
    ISNULL(vr.DeceasedTable,'dbo_QUAL') AS QualTable,
    ISNULL(vr.StudColumn,'dbo_CRED')    AS CredTable,
    ISNULL(vr.DeceasedColumn,'[dbo].[PQM]') AS PqmTable,
    ISNULL(vr.Status,'')           AS Status,
    vr.LastEditedByUserName, vr.LastEditedAt, vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = 46 AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var summary = DeserializeSummary(reader.IsDBNull(11) ? null : reader.GetString(11));

            var workspace = new Rule46WorkspaceStateViewModel
            {
                ClientId             = reader.GetInt32(1),
                RunId                = reader.GetInt32(0),
                Server               = reader.IsDBNull(2)  ? "" : reader.GetString(2),
                Database             = reader.IsDBNull(3)  ? "" : reader.GetString(3),
                StudTable            = reader.IsDBNull(4)  ? "dbo_STUD"    : reader.GetString(4),
                QualTable            = reader.IsDBNull(5)  ? "dbo_QUAL"    : reader.GetString(5),
                CredTable            = reader.IsDBNull(6)  ? "dbo_CRED"    : reader.GetString(6),
                PqmTable             = reader.IsDBNull(7)  ? "[dbo].[PQM]" : reader.GetString(7),
                CurrentStatus        = reader.IsDBNull(8)  ? "" : reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9)  ? null : reader.GetString(9),
                LastEditedAt         = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Driver               = "ODBC Driver 17 for SQL Server",
                Summary              = summary
            };
            // restore detailed config from summary if available
            if (summary != null)
            {
                workspace.StudKey         = summary.StudKey;
                workspace.StudFilterCol   = summary.StudFilterCol;
                workspace.StudFilterValue = summary.StudFilterValue;
                workspace.QualKey         = summary.QualKey;
                workspace.QualNameCol     = summary.QualNameCol;
                workspace.CredStudKey     = summary.CredStudKey;
                workspace.CredCourseCol   = summary.CredCourseCol;
                workspace.PqmNameCol      = summary.PqmNameCol;
                workspace.CurrentStatus   = summary.Status;
            }

            await reader.CloseAsync();

            workspace.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId!.Value, currentUserId);
            workspace.HasDataAnalystSignoff   = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            var roleSignoff = signoffs.FirstOrDefault(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff   = roleSignoff != null;
            workspace.CurrentUserSignoffComment = roleSignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved          = await IsWorkspaceSavedAsync(connection, workspace.RunId.Value);

            return workspace;
        }

        public async Task<Rule46RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID AND vr.RuleNumber = 46;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null) return null;

            var viewModel = new Rule46RunReviewViewModel
            {
                RunId          = reader.GetInt32(0),
                ClientId       = reader.GetInt32(1),
                IsCurrentRun   = !reader.IsDBNull(2) && reader.GetBoolean(2),
                EngagementName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                MaconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SourceServer   = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Summary        = summary
            };

            await reader.CloseAsync();

            viewModel.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, viewModel.ClientId, currentUserId.Value) ?? ""
                : "";
            viewModel.Signoffs              = await GetRunSignoffsAsync(connection, runId, currentUserId);
            viewModel.HasDataAnalystSignoff = viewModel.Signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            viewModel.CurrentUserHasSignedOff = viewModel.Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, viewModel.CurrentUserEngagementRole));
            viewModel.CanCurrentUserSignOff   = ValidationRunAccessPolicy.CanCompleteReviewSignoff(viewModel.CurrentUserEngagementRole, viewModel.CurrentUserEngagementRole, viewModel.HasDataAnalystSignoff);

            return viewModel;
        }

        public async Task<Rule46WorkspaceSaveResult> SaveWorkspaceAsync(Rule46ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                    return new Rule46WorkspaceSaveResult { Success = false, Error = "Run the validation first." };

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunInternalAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                    return new Rule46WorkspaceSaveResult { Success = false, Error = "The saved run could not be found for this engagement." };

                await EnsureClientNotArchivedAsync(connection, request.ClientId);
                await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 46);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                var previousHash    = await GetValidationRecordHashAsync(connection, request.RunId.Value);

                await using var cmd = connection.CreateConfiguredCommand();
                cmd.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer = @Server, AuditDatabase = @Database,
    StudTable = @StudTable, DeceasedTable = @QualTable, StudColumn = @CredTable, DeceasedColumn = @PqmTable,
    LastEditedByUserName = @LastEditedByUserName, LastEditedAt = GETDATE(), WorkspaceSavedAt = GETDATE(),
    PreviousHash = @PreviousHash, RecordHash = @RecordHash, Status = 'Needs Review', IsCurrent = 1
WHERE RunID = @RunID AND ClientID = @ClientID;";
                cmd.Parameters.AddWithValue("@RunID",               request.RunId.Value);
                cmd.Parameters.AddWithValue("@ClientID",            request.ClientId);
                cmd.Parameters.AddWithValue("@Server",              request.Server);
                cmd.Parameters.AddWithValue("@Database",            request.Database);
                cmd.Parameters.AddWithValue("@StudTable",           request.StudTable);
                cmd.Parameters.AddWithValue("@QualTable",           request.QualTable);
                cmd.Parameters.AddWithValue("@CredTable",           request.CredTable);
                cmd.Parameters.AddWithValue("@PqmTable",            request.PqmTable);
                cmd.Parameters.AddWithValue("@LastEditedByUserName",(object?)reviewerName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PreviousHash",        (object?)previousHash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RecordHash",          ComputeHash($"WorkspaceSave|Rule46|{request.RunId.Value}|{request.ClientId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await cmd.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail);
                return new Rule46WorkspaceSaveResult
                {
                    Success             = true,
                    Message             = clearedSignoffs > 0 ? "Workspace saved. Existing signoffs were removed." : "Workspace saved.",
                    SignoffsCleared     = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace           = workspace
                };
            }
            catch (Exception ex) { return new Rule46WorkspaceSaveResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule46WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunInternalAsync(connection, runId);
                if (!clientId.HasValue)
                    return new Rule46WorkspaceSaveResult { Success = false, Error = "Saved workspace was not found." };

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                await MarkPreviousRunsHistoricalAsync(connection, clientId.Value, 46);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash    = await GetValidationRecordHashAsync(connection, runId);

                await using var markEdit = connection.CreateConfiguredCommand();
                markEdit.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName = @LastEditedByUserName, LastEditedAt = GETDATE(), WorkspaceSavedAt = NULL,
    PreviousHash = @PreviousHash, RecordHash = @RecordHash, Status = 'Needs Review', IsCurrent = 1
WHERE RunID = @RunID;";
                markEdit.Parameters.AddWithValue("@RunID",              runId);
                markEdit.Parameters.AddWithValue("@LastEditedByUserName",(object?)reviewerName ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@PreviousHash",       (object?)previousHash ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@RecordHash",         ComputeHash($"BeginEdit|Rule46|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail);
                return new Rule46WorkspaceSaveResult
                {
                    Success             = true,
                    Message             = clearedSignoffs > 0 ? "Editing has begun. Signoffs were removed." : "Editing has begun.",
                    SignoffsCleared     = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace           = workspace
                };
            }
            catch (Exception ex) { return new Rule46WorkspaceSaveResult { Success = false, Error = ex.Message }; }
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue) throw new InvalidOperationException("Reviewer could not be resolved.");

            var clientId = await GetClientIdForRunInternalAsync(connection, runId);
            if (!clientId.HasValue) throw new InvalidOperationException("Validation run was not found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);
            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("Save the workspace before signing off.");

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(engagementRole))
                throw new InvalidOperationException("Only assigned data analysts, managers, and directors can sign off.");
            if (!string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) &&
                !await HasSignoffRoleAsync(connection, runId, "DataAnalyst"))
                throw new InvalidOperationException("The assigned data analyst must sign off first.");

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND ReviewerID = @ReviewerID)
    UPDATE dbo.ReviewSignoffs SET SignoffRole=@SignoffRole, ReviewType='Final', Comment=@Comment, SignedOffAt=GETDATE() WHERE RunID=@RunID AND ReviewerID=@ReviewerID;
ELSE
    INSERT INTO dbo.ReviewSignoffs (ClientID,RunID,ReviewerID,SignoffRole,ReviewType,Comment,SignedOffAt)
    VALUES (@ClientID,@RunID,@ReviewerID,@SignoffRole,'Final',@Comment,GETDATE());";
            command.Parameters.AddWithValue("@ClientID",   clientId.Value);
            command.Parameters.AddWithValue("@RunID",      runId);
            command.Parameters.AddWithValue("@ReviewerID", reviewerId.Value);
            command.Parameters.AddWithValue("@SignoffRole",engagementRole!);
            command.Parameters.AddWithValue("@Comment",    string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim());
            await command.ExecuteNonQueryAsync();
            await UpdateRunStatusFromSignoffsAsync(connection, runId);
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue) throw new InvalidOperationException("Reviewer could not be resolved.");
            var clientId = await GetClientIdForRunInternalAsync(connection, runId);
            if (!clientId.HasValue) throw new InvalidOperationException("Validation run was not found.");
            await EnsureClientNotArchivedAsync(connection, clientId.Value);
            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned analyst, manager, or director can remove signoff.");
            await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
        }

        // ── System DB helpers ──────────────────────────────────────────────────

        private static async Task<int?> GetSystemUserIdByEmailAsync(SqlConnection connection, string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            command.Parameters.AddWithValue("@Email", email);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private static async Task<string?> GetEngagementRoleAsync(SqlConnection connection, int clientId, int userId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 EngagementRole FROM dbo.UserClientAssignments WHERE ClientID = @ClientID AND UserID = @UserID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@UserID",   userId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private static async Task<List<RunSignoffViewModel>> GetRunSignoffsAsync(SqlConnection connection, int runId, int? currentUserId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT rs.SignoffID, ISNULL(rs.SignoffRole,'') AS SignoffRole,
       LTRIM(RTRIM(ISNULL(u.FirstName,'')+' '+ISNULL(u.LastName,''))) AS ReviewerName,
       ISNULL(u.Email,'') AS ReviewerEmail, ISNULL(rs.Comment,'') AS Comment, rs.SignedOffAt,
       CASE WHEN @CurrentUserID IS NOT NULL AND rs.ReviewerID=@CurrentUserID THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsCurrentUser
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.Users u ON u.UserID = rs.ReviewerID
WHERE rs.RunID = @RunID
ORDER BY CASE ISNULL(rs.SignoffRole,'') WHEN 'DataAnalyst' THEN 1 WHEN 'Manager' THEN 2 WHEN 'Director' THEN 3 ELSE 4 END, rs.SignedOffAt DESC;";
            command.Parameters.AddWithValue("@RunID",        runId);
            command.Parameters.AddWithValue("@CurrentUserID",currentUserId.HasValue ? currentUserId.Value : DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync();
            var signoffs = new List<RunSignoffViewModel>();
            while (await reader.ReadAsync())
                signoffs.Add(new RunSignoffViewModel
                {
                    Id            = reader.GetInt32(0),
                    SignoffRole   = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    ReviewerName  = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    ReviewerEmail = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Comment       = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    SignedOffAt   = reader.IsDBNull(5) ? DateTime.UtcNow : reader.GetDateTime(5),
                    IsCurrentUser = !reader.IsDBNull(6) && reader.GetBoolean(6)
                });
            return signoffs;
        }

        private static async Task<bool> IsWorkspaceSavedAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM dbo.ValidationRuns WHERE RunID = @RunID
    AND (WorkspaceSavedAt IS NOT NULL OR EXISTS (
        SELECT 1 FROM dbo.ReviewSignoffs rs WHERE rs.RunID = ValidationRuns.RunID AND rs.SignoffRole = 'DataAnalyst'))
) THEN 1 ELSE 0 END;";
            command.Parameters.AddWithValue("@RunID", runId);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }

        private static async Task<bool> HasSignoffRoleAsync(SqlConnection connection, int runId, string role)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole=@Role) THEN 1 ELSE 0 END;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@Role",  role);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }

        private static async Task<string?> GetValidationRecordHashAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private static async Task<string?> GetLatestValidationHashAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE ClientID=@ClientID AND RuleNumber=@RuleNumber AND RecordHash IS NOT NULL ORDER BY RunTimestamp DESC, RunID DESC;";
            command.Parameters.AddWithValue("@ClientID",   clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private static async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 Status FROM dbo.Clients WHERE ClientID = @ClientID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            var status = Convert.ToString(await command.ExecuteScalarAsync());
            if (string.Equals(status, "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Archived engagements are read-only.");
        }

        private static async Task<int?> GetClientIdForRunInternalAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private static async Task MarkPreviousRunsHistoricalAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET IsCurrent = 0 WHERE ClientID=@ClientID AND RuleNumber=@RuleNumber AND IsCurrent=1;";
            command.Parameters.AddWithValue("@ClientID",   clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<int> ClearSignoffsAndFlagForReviewAsync(SqlConnection connection, int runId)
        {
            await using var countCmd = connection.CreateConfiguredCommand();
            countCmd.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            countCmd.Parameters.AddWithValue("@RunID", runId);
            var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            await using var deleteCmd = connection.CreateConfiguredCommand();
            deleteCmd.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            deleteCmd.Parameters.AddWithValue("@RunID", runId);
            await deleteCmd.ExecuteNonQueryAsync();
            await SetRunStatusAsync(connection, runId, "Needs Review");
            return count;
        }

        private static async Task UpdateRunStatusFromSignoffsAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole='DataAnalyst') THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole='Manager')     THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole='Director')    THEN 1 ELSE 0 END;";
            command.Parameters.AddWithValue("@RunID", runId);
            await using var reader = await command.ExecuteReaderAsync();
            var hasAll = await reader.ReadAsync() && reader.GetInt32(0) == 1 && reader.GetInt32(1) == 1 && reader.GetInt32(2) == 1;
            await reader.CloseAsync();
            await SetRunStatusAsync(connection, runId, hasAll ? "Reviewed and Completed" : "Needs Review");
        }

        private static async Task SetRunStatusAsync(SqlConnection connection, int runId, string status)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET Status=@Status WHERE RunID=@RunID;";
            command.Parameters.AddWithValue("@RunID",  runId);
            command.Parameters.AddWithValue("@Status", status);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<int> CountAsync(SqlConnection conn, string sql)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandTimeout = SqlLargeDataExtensions.LargeDataCommandTimeoutSeconds;
            cmd.CommandText    = sql;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // ── Utilities ──────────────────────────────────────────────────────────

        private static string Sanitise(string name) =>
            name.Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();

        private static string SanitisePqm(string name)
        {
            var s = name.Trim();
            if (s.StartsWith("[dbo].", StringComparison.OrdinalIgnoreCase) || s.Contains(".["))
                return s.Replace("'", "").Replace(";", "").Trim();
            return $"[{Sanitise(s)}]";
        }

        private static void ValidateObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Table or column name is required.");
            foreach (var bad in new[] { ";", "'", "\"", "--", "/*", "*/" })
                if (value.Contains(bad, StringComparison.Ordinal))
                    throw new InvalidOperationException("Unsafe table or column name.");
        }

        private static void ValidateRequest(Rule46ValidationRequest r)
        {
            ValidateObjectName(r.StudTable); ValidateObjectName(r.StudKey); ValidateObjectName(r.StudFilterCol);
            ValidateObjectName(r.QualTable); ValidateObjectName(r.QualKey); ValidateObjectName(r.QualNameCol);
            ValidateObjectName(r.CredTable); ValidateObjectName(r.CredStudKey); ValidateObjectName(r.CredCourseCol);
            ValidateObjectName(r.PqmNameCol);
        }

        private static string? FindFirst(IEnumerable<string> values, string[] exactMatches, string[] containsMatches)
        {
            foreach (var exact in exactMatches)
            {
                var m = values.FirstOrDefault(v => v.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(m)) return m;
            }
            foreach (var fragment in containsMatches)
            {
                var m = values.FirstOrDefault(v => v.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(m)) return m;
            }
            return values.FirstOrDefault();
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
        }

        private static Rule46ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonConvert.DeserializeObject<Rule46ValidationSummary>(ValidationPayloadCodec.Decode(json)); }
            catch { return null; }
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager",     StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director",    StringComparison.OrdinalIgnoreCase);
    }
}
