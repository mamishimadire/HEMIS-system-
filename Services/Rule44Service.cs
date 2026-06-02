using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using HemisAudit.Helpers;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule44Service : IRule44Service
    {
        private const int BrowserPreviewRowLimit  = 10;
        private const int FailRowSaveLimit        = 5000;
        private const int PassSampleSaveLimit     = 100;
        private const int SqlCommandTimeoutSeconds = SqlLargeDataExtensions.LargeDataCommandTimeoutSeconds;

        private readonly IConfiguration _configuration;

        public Rule44Service(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // ── Connection ────────────────────────────────────────────────────────

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
                using (var sp = Process.Start(new ProcessStartInfo("sqllocaldb", $"start \"{instance}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!)
                { sp.WaitForExit(8000); }
                using var ip = Process.Start(new ProcessStartInfo("sqllocaldb", $"info \"{instance}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
                var output = ip.StandardOutput.ReadToEnd();
                ip.WaitForExit(3000);
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

        // ── Discovery ─────────────────────────────────────────────────────────

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

        public async Task<Rule44TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
        {
            try
            {
                await using var conn = new SqlConnection(BuildConnectionString(server, database, driver));
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME;";
                await using var reader = await cmd.ExecuteReaderAsync();
                var tables = new List<string>();
                while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
                return new Rule44TableDiscoveryResult
                {
                    Success       = true,
                    Tables        = tables,
                    AutoStudTable = FindFirst(tables, ["dbo_STUD", "STUD"], ["stud"]),
                    AutoQualTable = FindFirst(tables, ["dbo_QUAL", "QUAL"], ["qual"]),
                    AutoPqmTable  = FindFirst(tables, ["PQM"], ["pqm"])
                };
            }
            catch (Exception ex) { return new Rule44TableDiscoveryResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule44ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                tableName = (tableName ?? "").Trim();
                ValidateObjectName(tableName);
                await using var conn = new SqlConnection(BuildConnectionString(server, database, driver));
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @T ORDER BY ORDINAL_POSITION;";
                cmd.Parameters.AddWithValue("@T", tableName);
                await using var reader = await cmd.ExecuteReaderAsync();
                var cols = new List<string>();
                while (await reader.ReadAsync()) if (!reader.IsDBNull(0)) cols.Add(reader.GetString(0));
                return new Rule44ColumnDiscoveryResult { Success = true, Columns = cols };
            }
            catch (Exception ex) { return new Rule44ColumnDiscoveryResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule44VerifyResult> VerifyTablesAsync(Rule44VerifyRequest request)
        {
            try
            {
                NormalizeRequest(request.StudTable, request.QualTable, request.PqmTable, request.ColumnMapping, out var st, out var qt, out var pt, out var m);
                ValidateObjectName(st); ValidateObjectName(qt); ValidateObjectName(pt);
                var pgList = ParsePgTypes(request.PgTypesText);

                await using var conn = new SqlConnection(BuildConnectionString(request.Server, request.Database, request.Driver));
                await conn.OpenAsync();

                var pgSql = BuildPgInList(pgList);
                var countSql = $@"
SELECT COUNT_BIG(*) FROM [{Sanitise(st)}] S
INNER JOIN [{Sanitise(qt)}] Q ON LTRIM(RTRIM(CAST(S.[{m.StudQualCodeCol}] AS nvarchar(200)))) = LTRIM(RTRIM(CAST(Q.[{m.QualQualCodeCol}] AS nvarchar(200))))
WHERE {(string.IsNullOrWhiteSpace(pgSql) ? "1=0" : $"LTRIM(RTRIM(CAST(Q.[{m.QualTypeCol}] AS nvarchar(20)))) IN ({pgSql})")};";

                await using var countCmd = conn.CreateConfiguredCommand();
                countCmd.CommandTimeout = SqlCommandTimeoutSeconds;
                countCmd.CommandText    = countSql;
                var mastersDoctCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

                await using var pqmCmd = conn.CreateConfiguredCommand();
                pqmCmd.CommandTimeout = SqlCommandTimeoutSeconds;
                pqmCmd.CommandText    = $"SELECT COUNT_BIG(*) FROM [{Sanitise(pt)}];";
                var pqmCount = Convert.ToInt32(await pqmCmd.ExecuteScalarAsync());

                return new Rule44VerifyResult { Success = true, MastersDoctCount = mastersDoctCount, PqmCount = pqmCount };
            }
            catch (Exception ex) { return new Rule44VerifyResult { Success = false, Error = ex.Message }; }
        }

        // ── Validation ────────────────────────────────────────────────────────

        public async Task<Rule44ValidationSummary> RunValidationAsync(Rule44ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                NormalizeRequest(request.StudTable, request.QualTable, request.PqmTable, request.ColumnMapping, out var st, out var qt, out var pt, out var m);
                request.StudTable = st; request.QualTable = qt; request.PqmTable = pt;

                var summary = await AnalyseAsync(request, m);

                if (summary.Success && request.ClientId > 0)
                {
                    try { summary.SavedRunId = await SaveValidationRunAsync(request, summary, userEmail, userName); }
                    catch (Exception ex)
                    {
                        summary.Success = false;
                        summary.Error   = $"Analysis completed, but the run could not be saved: {ex.Message}";
                        return summary;
                    }
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex) { return new Rule44ValidationSummary { Success = false, Error = ex.Message }; }
        }

        private async Task<Rule44ValidationSummary> AnalyseAsync(Rule44ValidationRequest request, Rule44ColumnMapping m)
        {
            var pgList = ParsePgTypes(request.PgTypesText);
            if (pgList.Count == 0)
                return new Rule44ValidationSummary { Success = false, Error = "No valid PG type codes were specified." };

            var pgSql = BuildPgInList(pgList);
            var sql   = BuildValidationSql(request.StudTable, request.QualTable, request.PqmTable, m, pgSql);

            await using var conn = new SqlConnection(BuildConnectionString(request.Server, request.Database, request.Driver));
            await conn.OpenAsync();

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandTimeout = SqlCommandTimeoutSeconds;
            cmd.CommandText    = sql;
            await using var reader = await cmd.ExecuteReaderAsync();

            var passRows = new List<Rule44ReviewRow>();
            var failRows = new List<Rule44ReviewRow>();
            int rowNo = 0;

            while (await reader.ReadAsync())
            {
                rowNo++;
                var result = (reader.IsDBNull(8) ? "" : reader.GetString(8)).Trim().ToUpper();
                var row = new Rule44ReviewRow
                {
                    RowNumber             = rowNo,
                    StudentNo             = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim(),
                    StudentId             = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                    QualCode              = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim(),
                    StudStatus            = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim(),
                    QualName              = reader.IsDBNull(4) ? "" : reader.GetString(4).Trim(),
                    QualType              = reader.IsDBNull(5) ? "" : reader.GetString(5).Trim(),
                    StudResearchTime      = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim(),
                    PqmResearchTime       = reader.IsDBNull(7) ? "" : reader.GetString(7).Trim(),
                    ValidationResult      = result,
                    ValidationExplanation = reader.IsDBNull(9) ? "" : reader.GetString(9).Trim()
                };

                if (result == "PASS")
                    passRows.Add(row);
                else
                    failRows.Add(row);
            }

            var totalCount  = rowNo;
            var passCount   = passRows.Count;
            var failCount   = failRows.Count(r => r.ValidationResult == "FAIL");
            var missingCount= failRows.Count(r => r.ValidationResult != "FAIL");
            var excRate     = totalCount == 0 ? 0m : Math.Round((decimal)(totalCount - passCount) / totalCount * 100m, 2);

            return new Rule44ValidationSummary
            {
                Success          = true,
                Timestamp        = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database         = request.Database,
                StudTable        = request.StudTable,
                QualTable        = request.QualTable,
                PqmTable         = request.PqmTable,
                PgTypesText      = request.PgTypesText,
                ColumnMapping    = m,
                ClientId         = request.ClientId,
                TotalCount       = totalCount,
                PassCount        = passCount,
                FailCount        = failCount,
                MissingPqmCount  = missingCount,
                ExceptionRate    = excRate,
                Status           = (totalCount - passCount == 0) ? "PASS" : "FAIL",
                PassRows         = passRows.Take(PassSampleSaveLimit).ToList(),
                FailRows         = failRows.Take(FailRowSaveLimit).ToList()
            };
        }

        private static string BuildValidationSql(string studTable, string qualTable, string pqmTable, Rule44ColumnMapping m, string pgSql)
        {
            var st = Sanitise(studTable);
            var qt = Sanitise(qualTable);
            var pt = Sanitise(pqmTable);

            return $@"
WITH FilteredStudents AS (
    SELECT
        LTRIM(RTRIM(CAST(S.[{m.StudStudentNoCol}]    AS nvarchar(100)))) AS StudentNo,
        LTRIM(RTRIM(CAST(S.[{m.StudIdCol}]           AS nvarchar(100)))) AS StudentId,
        LTRIM(RTRIM(CAST(S.[{m.StudQualCodeCol}]     AS nvarchar(200)))) AS QualCode,
        LTRIM(RTRIM(CAST(S.[{m.StudStatusCol}]       AS nvarchar(50))))  AS StudStatus,
        LTRIM(RTRIM(CAST(S.[{m.StudResearchTimeCol}] AS nvarchar(50))))  AS StudResearchTime,
        LTRIM(RTRIM(CAST(Q.[{m.QualQualCodeCol}]     AS nvarchar(200)))) AS QualQualCode,
        LTRIM(RTRIM(CAST(Q.[{m.QualNameCol}]         AS nvarchar(500)))) AS QualName,
        LTRIM(RTRIM(CAST(Q.[{m.QualTypeCol}]         AS nvarchar(20))))  AS QualType
    FROM [{st}] S
    INNER JOIN [{qt}] Q
        ON LTRIM(RTRIM(CAST(S.[{m.StudQualCodeCol}] AS nvarchar(200))))
         = LTRIM(RTRIM(CAST(Q.[{m.QualQualCodeCol}] AS nvarchar(200))))
    WHERE {(string.IsNullOrWhiteSpace(pgSql) ? "1=0" : $"LTRIM(RTRIM(CAST(Q.[{m.QualTypeCol}] AS nvarchar(20)))) IN ({pgSql})")}
)
SELECT
    F.StudentNo,
    F.StudentId,
    F.QualCode,
    F.StudStatus,
    F.QualName,
    F.QualType,
    F.StudResearchTime,
    ISNULL(LTRIM(RTRIM(CAST(P.[{m.PqmResearchTimeCol}] AS nvarchar(50)))), '') AS PqmResearchTime,
    CASE
        WHEN P.[{m.PqmNameCol}] IS NULL THEN 'MISSING_PQM'
        WHEN F.StudResearchTime = LTRIM(RTRIM(CAST(P.[{m.PqmResearchTimeCol}] AS nvarchar(50)))) THEN 'PASS'
        ELSE 'FAIL'
    END AS ValidationResult,
    CASE
        WHEN P.[{m.PqmNameCol}] IS NULL
            THEN 'No PQM record found for qualification: ' + ISNULL(F.QualName, '')
        WHEN F.StudResearchTime = LTRIM(RTRIM(CAST(P.[{m.PqmResearchTimeCol}] AS nvarchar(50))))
            THEN 'PASS: STUD._073 (' + F.StudResearchTime + ') agrees with PQM.' + '{m.PqmResearchTimeCol}' + ' (' + ISNULL(LTRIM(RTRIM(CAST(P.[{m.PqmResearchTimeCol}] AS nvarchar(50)))), '') + ')'
        ELSE 'FAIL: STUD._073 (' + F.StudResearchTime + ') disagrees with PQM.' + '{m.PqmResearchTimeCol}' + ' (' + ISNULL(LTRIM(RTRIM(CAST(P.[{m.PqmResearchTimeCol}] AS nvarchar(50)))), '') + ')'
    END AS ValidationExplanation
FROM FilteredStudents F
LEFT JOIN [{pt}] P
    ON F.QualName = LTRIM(RTRIM(CAST(P.[{m.PqmNameCol}] AS nvarchar(500))))
ORDER BY
    CASE WHEN P.[{m.PqmNameCol}] IS NULL THEN 0
         WHEN F.StudResearchTime <> LTRIM(RTRIM(CAST(P.[{m.PqmResearchTimeCol}] AS nvarchar(50)))) THEN 1
         ELSE 2 END,
    F.StudentNo;";
        }

        // ── SQL generation ────────────────────────────────────────────────────

        public string GenerateSql(Rule44ValidationRequest request)
        {
            NormalizeRequest(request.StudTable, request.QualTable, request.PqmTable, request.ColumnMapping, out var st, out var qt, out var pt, out var m);
            var pgList = ParsePgTypes(request.PgTypesText);
            var pgSql  = BuildPgInList(pgList);

            return $@"-- ============================================================
-- HEMIS RULE 44 – Masters / PhD Research Time Validation
-- Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}
-- Database  : {request.Database}
-- Tables    : [{Sanitise(st)}] STUD  |  [{Sanitise(qt)}] QUAL  |  [{Sanitise(pt)}] PQM
-- Display   : STUD.{m.StudStudentNoCol}, STUD.{m.StudIdCol}, STUD.{m.StudQualCodeCol}, STUD.{m.StudStatusCol}, STUD.{m.StudResearchTimeCol}
-- Filter    : QUAL.{m.QualTypeCol} IN ({string.Join(", ", pgList)})
-- Compare   : STUD.{m.StudResearchTimeCol}  vs  PQM.{m.PqmResearchTimeCol}
-- ============================================================
{BuildValidationSql(st, qt, pt, m, pgSql)}";
        }

        // ── Save / Load ───────────────────────────────────────────────────────

        private async Task<int> SaveValidationRunAsync(Rule44ValidationRequest request, Rule44ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 44);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 44);
            var persisted    = CloneSummaryForPersistence(summary);
            ApplyBrowserPreview(persisted);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
INSERT INTO dbo.ValidationRuns
(
    ClientID, UserID, RuleNumber, RuleName, Status, TotalRecords, PassCount, FailCount, ExceptionRate, RunTimestamp,
    HemisServer, AuditDatabase, StudTable, DeceasedTable, StudColumn, DeceasedColumn,
    ExceptionsJSON, ResultsJSON, RunByUserName, LastEditedByUserName, LastEditedAt, PreviousHash, RecordHash, IsCurrent
)
VALUES
(
    @ClientID, @UserID, 44, 'Masters/PhD Research Time Validation', @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @QualTable, @PqmTable, @PgTypesText,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";

            command.Parameters.AddWithValue("@ClientID",     request.ClientId);
            command.Parameters.AddWithValue("@UserID",       systemUserId.Value);
            command.Parameters.AddWithValue("@Status",       summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalCount);
            command.Parameters.AddWithValue("@PassCount",    summary.PassCount);
            command.Parameters.AddWithValue("@FailCount",    summary.TotalCount - summary.PassCount);
            command.Parameters.AddWithValue("@ExceptionRate",summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer",  request.Server);
            command.Parameters.AddWithValue("@AuditDatabase",request.Database);
            command.Parameters.AddWithValue("@StudTable",    request.StudTable);
            command.Parameters.AddWithValue("@QualTable",    request.QualTable);
            command.Parameters.AddWithValue("@PqmTable",     request.PqmTable);
            command.Parameters.AddWithValue("@PgTypesText",  request.PgTypesText);
            command.Parameters.AddWithValue("@ExceptionsJSON",ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persisted.FailRows)));
            command.Parameters.AddWithValue("@ResultsJSON",  ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persisted)));
            command.Parameters.AddWithValue("@RunByUserName",(object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCmd = connection.CreateConfiguredCommand();
            hashCmd.CommandText = "UPDATE dbo.ValidationRuns SET RecordHash = @RecordHash WHERE RunID = @RunID;";
            hashCmd.Parameters.AddWithValue("@RunID", runId);
            hashCmd.Parameters.AddWithValue("@RecordHash", ComputeHash($"ValidationRun|Rule44|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCmd.ExecuteNonQueryAsync();

            return runId;
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await using var command    = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID AND RuleNumber = 44;";
            command.Parameters.AddWithValue("@RunID", runId);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        public async Task<Rule44WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID, vr.ClientID,
    ISNULL(vr.HemisServer,'')   AS HemisServer,
    ISNULL(vr.AuditDatabase,'') AS AuditDatabase,
    ISNULL(vr.StudTable,'')     AS StudTable,
    ISNULL(vr.DeceasedTable,'') AS QualTable,
    ISNULL(vr.StudColumn,'')    AS PqmTable,
    ISNULL(vr.DeceasedColumn,'') AS PgTypesText,
    ISNULL(vr.Status,'')        AS Status,
    vr.LastEditedByUserName, vr.LastEditedAt, vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = 44 AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var summary = DeserializeSummary(reader.IsDBNull(11) ? null : reader.GetString(11));
            if (summary != null) ApplyBrowserPreview(summary);

            var workspace = new Rule44WorkspaceStateViewModel
            {
                ClientId         = reader.GetInt32(1),
                RunId            = reader.GetInt32(0),
                Server           = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database         = reader.IsDBNull(3) ? "" : reader.GetString(3),
                StudTable        = reader.IsDBNull(4) ? "dbo_STUD" : reader.GetString(4),
                QualTable        = reader.IsDBNull(5) ? "dbo_QUAL"  : reader.GetString(5),
                PqmTable         = reader.IsDBNull(6) ? "PQM"       : reader.GetString(6),
                PgTypesText      = reader.IsDBNull(7) ? "07, 27, 28, 49, 72, 73, 08, 30, 50, 74, 75" : reader.GetString(7),
                CurrentStatus    = reader.IsDBNull(8) ? "" : reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9)  ? null : reader.GetString(9),
                LastEditedAt         = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Summary          = summary,
                ColumnMapping    = summary?.ColumnMapping ?? new Rule44ColumnMapping()
            };

            if (summary != null) workspace.CurrentStatus = summary.Status;
            await reader.CloseAsync();

            workspace.Driver = "ODBC Driver 17 for SQL Server";
            workspace.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId!.Value, currentUserId);
            workspace.HasDataAnalystSignoff  = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            var mySignoff = signoffs.FirstOrDefault(s =>
                ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff    = mySignoff != null;
            workspace.CurrentUserSignoffComment  = mySignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved           = await IsWorkspaceSavedAsync(connection, workspace.RunId!.Value);

            if (workspace.Summary != null) workspace.Summary.SavedRunId = workspace.RunId;
            return workspace;
        }

        public async Task<Rule44RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID AND vr.RuleNumber = 44;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null) return null;
            ApplyBrowserPreview(summary);

            var clientId = reader.GetInt32(1);
            var review   = new Rule44RunReviewViewModel
            {
                RunId         = reader.GetInt32(0),
                ClientId      = clientId,
                IsCurrentRun  = !reader.IsDBNull(2) && reader.GetBoolean(2),
                EngagementName= reader.IsDBNull(3) ? "" : reader.GetString(3),
                MaconomyNumber= reader.IsDBNull(4) ? "" : reader.GetString(4),
                SourceServer  = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Summary       = summary
            };
            await reader.CloseAsync();

            summary.SavedRunId = review.RunId;
            review.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";

            var signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            review.Signoffs             = signoffs;
            review.HasDataAnalystSignoff= signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            review.GeneratedSql         = GenerateSql(new Rule44ValidationRequest
            {
                ClientId    = clientId,
                Database    = summary.Database,
                StudTable   = summary.StudTable,
                QualTable   = summary.QualTable,
                PqmTable    = summary.PqmTable,
                PgTypesText = summary.PgTypesText,
                ColumnMapping = summary.ColumnMapping
            });
            return review;
        }

        public async Task<Rule44WorkspaceSaveResult> SaveWorkspaceAsync(Rule44ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                await EnsureClientNotArchivedAsync(connection, request.ClientId);
                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                    return new Rule44WorkspaceSaveResult { Success = false, Error = "Your account could not be resolved in the system database." };

                if (!request.RunId.HasValue || request.RunId.Value <= 0)
                    return new Rule44WorkspaceSaveResult { Success = false, Error = "No saved run exists. Run Rule 44 first." };

                var cleared = await ClearSignoffsAsync(connection, request.RunId.Value);
                var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 44);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns SET
    HemisServer          = @HemisServer,
    AuditDatabase        = @AuditDatabase,
    StudTable            = @StudTable,
    DeceasedTable        = @QualTable,
    StudColumn           = @PqmTable,
    DeceasedColumn       = @PgTypesText,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    WorkspaceSavedAt     = GETDATE(),
    Status               = 'Needs Review',
    RecordHash           = @RecordHash
WHERE RunID = @RunID AND RuleNumber = 44;";
                command.Parameters.AddWithValue("@HemisServer",         request.Server);
                command.Parameters.AddWithValue("@AuditDatabase",        request.Database);
                command.Parameters.AddWithValue("@StudTable",            request.StudTable);
                command.Parameters.AddWithValue("@QualTable",            request.QualTable);
                command.Parameters.AddWithValue("@PqmTable",             request.PqmTable);
                command.Parameters.AddWithValue("@PgTypesText",          request.PgTypesText);
                command.Parameters.AddWithValue("@LastEditedByUserName", reviewerName ?? reviewerEmail);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($"WorkspaceSave|Rule44|{request.RunId.Value}|{request.ClientId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail);
                return new Rule44WorkspaceSaveResult
                {
                    Success            = true,
                    Message            = cleared > 0 ? $"Workspace saved. {cleared} signoff(s) were cleared." : "Workspace saved.",
                    SignoffsCleared    = cleared > 0,
                    ClearedSignoffCount= cleared,
                    Workspace          = workspace
                };
            }
            catch (Exception ex) { return new Rule44WorkspaceSaveResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule44WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                    return new Rule44WorkspaceSaveResult { Success = false, Error = "Saved run not found." };

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                var cleared      = await ClearSignoffsAsync(connection, runId);
                var previousHash = await GetLatestValidationHashAsync(connection, clientId.Value, 44);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns SET
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    RecordHash           = @RecordHash
WHERE RunID = @RunID;";
                command.Parameters.AddWithValue("@LastEditedByUserName", reviewerName ?? reviewerEmail);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($"BeginWorkspaceEdit|Rule44|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                command.Parameters.AddWithValue("@RunID", runId);
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail);
                return new Rule44WorkspaceSaveResult
                {
                    Success            = true,
                    Message            = cleared > 0 ? $"Workspace unlocked for editing. {cleared} signoff(s) cleared." : "Workspace unlocked for editing.",
                    SignoffsCleared    = cleared > 0,
                    ClearedSignoffCount= cleared,
                    Workspace          = workspace
                };
            }
            catch (Exception ex) { return new Rule44WorkspaceSaveResult { Success = false, Error = ex.Message }; }
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail)
                ?? throw new InvalidOperationException("Your account could not be resolved in the system database.");
            var role = await GetRunEngagementRoleAsync(connection, runId, reviewerId)
                ?? throw new InvalidOperationException("You are not assigned to this engagement.");
            var clientId = await GetClientIdForRunAsync(connection, runId)
                ?? throw new InvalidOperationException("Validation run not found.");

            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND ReviewerID = @ReviewerID)
    UPDATE dbo.ReviewSignoffs SET SignoffRole=@Role, ReviewType='Final', Comment=@Comment, SignedOffAt=GETDATE() WHERE RunID=@RunID AND ReviewerID=@ReviewerID;
ELSE
    INSERT INTO dbo.ReviewSignoffs (ClientID,RunID,ReviewerID,SignoffRole,ReviewType,Comment,SignedOffAt)
    VALUES (@ClientID,@RunID,@ReviewerID,@Role,'Final',@Comment,GETDATE());";
            cmd.Parameters.AddWithValue("@ClientID",   clientId);
            cmd.Parameters.AddWithValue("@RunID",      runId);
            cmd.Parameters.AddWithValue("@ReviewerID", reviewerId);
            cmd.Parameters.AddWithValue("@Comment",    string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim());
            cmd.Parameters.AddWithValue("@Role",       role);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail)
                ?? throw new InvalidOperationException("Your account could not be resolved.");

            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND ReviewerID = @ReviewerID;";
            cmd.Parameters.AddWithValue("@RunID",      runId);
            cmd.Parameters.AddWithValue("@ReviewerID", reviewerId);
            await cmd.ExecuteNonQueryAsync();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void ApplyBrowserPreview(Rule44ValidationSummary? s)
        {
            if (s == null) return;
            var failTake = Math.Min(s.FailRows.Count, BrowserPreviewRowLimit);
            var passTake = Math.Min(s.PassRows.Count, Math.Max(0, BrowserPreviewRowLimit - failTake));
            s.FailRows   = s.FailRows.Take(failTake).ToList();
            s.PassRows   = s.PassRows.Take(passTake).ToList();
            s.IsPreviewOnly = (s.FailRows.Count + s.PassRows.Count) < s.TotalCount;
            s.PreviewLimit  = BrowserPreviewRowLimit;
        }

        private static Rule44ValidationSummary CloneSummaryForPersistence(Rule44ValidationSummary src)
            => JsonConvert.DeserializeObject<Rule44ValidationSummary>(JsonConvert.SerializeObject(src)) ?? new Rule44ValidationSummary();

        private static Rule44ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule44ValidationSummary>(decoded);
            }
            catch { return null; }
        }

        private static void NormalizeRequest(
            string studTableIn, string qualTableIn, string pqmTableIn, Rule44ColumnMapping? mappingIn,
            out string studTable, out string qualTable, out string pqmTable, out Rule44ColumnMapping m)
        {
            studTable = (studTableIn ?? "dbo_STUD").Trim();
            qualTable = (qualTableIn ?? "dbo_QUAL").Trim();
            pqmTable  = (pqmTableIn  ?? "PQM").Trim();
            m         = mappingIn ?? new Rule44ColumnMapping();
            m.StudStudentNoCol    = ColOrDef(m.StudStudentNoCol,    "_007");
            m.StudIdCol           = ColOrDef(m.StudIdCol,           "_008");
            m.StudQualCodeCol     = ColOrDef(m.StudQualCodeCol,     "_001");
            m.StudStatusCol       = ColOrDef(m.StudStatusCol,       "_010");
            m.StudResearchTimeCol = ColOrDef(m.StudResearchTimeCol, "_073");
            m.QualQualCodeCol     = ColOrDef(m.QualQualCodeCol,     "_001");
            m.QualNameCol         = ColOrDef(m.QualNameCol,         "_003");
            m.QualTypeCol         = ColOrDef(m.QualTypeCol,         "_005");
            m.PqmNameCol          = ColOrDef(m.PqmNameCol,         "Authorised_Qualification_Name");
            m.PqmResearchTimeCol  = ColOrDef(m.PqmResearchTimeCol, "Research_1");
            ValidateObjectName(studTable); ValidateObjectName(qualTable); ValidateObjectName(pqmTable);
            ValidateObjectName(m.StudStudentNoCol);
            ValidateObjectName(m.StudIdCol);
            ValidateObjectName(m.StudQualCodeCol);
            ValidateObjectName(m.StudStatusCol);
            ValidateObjectName(m.StudResearchTimeCol);
            ValidateObjectName(m.QualQualCodeCol);
            ValidateObjectName(m.QualNameCol);
            ValidateObjectName(m.QualTypeCol);
            ValidateObjectName(m.PqmNameCol);
            ValidateObjectName(m.PqmResearchTimeCol);
        }

        private static string ColOrDef(string? v, string def) => string.IsNullOrWhiteSpace(v) ? def : v.Trim();

        private static List<string> ParsePgTypes(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
                       .Select(t => t.Trim())
                       .Where(t => t.Length > 0)
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .ToList();
        }

        private static string BuildPgInList(IEnumerable<string> types)
            => string.Join(", ", types.Select(t => $"'{t.Replace("'", "''")}'"));

        private static string? FindFirst(List<string> tables, string[] exactMatches, string[] partials)
        {
            foreach (var e in exactMatches)
                if (tables.Any(t => string.Equals(t, e, StringComparison.OrdinalIgnoreCase))) return e;
            foreach (var p in partials)
            {
                var match = tables.FirstOrDefault(t => t.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) return match;
            }
            return null;
        }

        private static string Sanitise(string name) => name.Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();

        private static void ValidateObjectName(string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Object name cannot be blank.");
            if (name.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '-')))
                throw new InvalidOperationException($"Invalid object name '{name}'.");
        }

        private static string ComputeHash(string input)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(input);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }

        // ── System DB shared helpers ──────────────────────────────────────────

        private async Task<int?> GetSystemUserIdByEmailAsync(SqlConnection connection, string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            cmd.Parameters.AddWithValue("@Email", email);
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        private async Task<string?> GetEngagementRoleAsync(SqlConnection connection, int clientId, int userId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 EngagementRole FROM dbo.UserClientAssignments WHERE ClientID = @ClientID AND UserID = @UserID;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            cmd.Parameters.AddWithValue("@UserID",   userId);
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : result.ToString();
        }

        private async Task<string?> GetRunEngagementRoleAsync(SqlConnection connection, int runId, int userId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT TOP 1 uca.EngagementRole FROM dbo.UserClientAssignments uca
INNER JOIN dbo.ValidationRuns vr ON vr.ClientID = uca.ClientID
WHERE vr.RunID = @RunID AND uca.UserID = @UserID;";
            cmd.Parameters.AddWithValue("@RunID",   runId);
            cmd.Parameters.AddWithValue("@UserID",  userId);
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : result.ToString();
        }

        private async Task<int?> GetClientIdForRunAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID AND RuleNumber = 44;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        private async Task MarkPreviousRunsHistoricalAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "UPDATE dbo.ValidationRuns SET IsCurrent = 0 WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber AND IsCurrent = 1;";
            cmd.Parameters.AddWithValue("@ClientID",    clientId);
            cmd.Parameters.AddWithValue("@RuleNumber",  ruleNumber);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 Status FROM dbo.Clients WHERE ClientID = @ClientID;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            var result = await cmd.ExecuteScalarAsync();
            if (string.Equals(Convert.ToString(result), "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This engagement is archived and cannot accept new validation runs.");
        }

        private async Task<string?> GetLatestValidationHashAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber ORDER BY RunTimestamp DESC, RunID DESC;";
            cmd.Parameters.AddWithValue("@ClientID",   clientId);
            cmd.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : result.ToString();
        }

        private async Task<List<RunSignoffViewModel>> GetRunSignoffsAsync(SqlConnection connection, int runId, int? currentUserId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT rs.SignoffRole, rs.Comment, rs.SignedOffAt, u.Email,
       LTRIM(RTRIM(ISNULL(u.FirstName,'')+' '+ISNULL(u.LastName,''))) AS ReviewerName,
       CASE WHEN @CurrentUserID IS NOT NULL AND rs.ReviewerID = @CurrentUserID THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsCurrentUser
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.Users u ON u.UserID = rs.ReviewerID
WHERE rs.RunID = @RunID
ORDER BY CASE ISNULL(rs.SignoffRole,'') WHEN 'DataAnalyst' THEN 1 WHEN 'Manager' THEN 2 WHEN 'Director' THEN 3 ELSE 4 END, rs.SignedOffAt DESC;";
            cmd.Parameters.AddWithValue("@RunID",        runId);
            cmd.Parameters.AddWithValue("@CurrentUserID",currentUserId.HasValue ? currentUserId.Value : DBNull.Value);
            await using var reader = await cmd.ExecuteReaderAsync();
            var list = new List<RunSignoffViewModel>();
            while (await reader.ReadAsync())
            {
                list.Add(new RunSignoffViewModel
                {
                    SignoffRole   = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Comment       = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    SignedOffAt   = reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2),
                    ReviewerEmail = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    ReviewerName  = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    IsCurrentUser = !reader.IsDBNull(5) && reader.GetBoolean(5)
                });
            }
            return list;
        }

        private async Task<int> ClearSignoffsAsync(SqlConnection connection, int runId)
        {
            await using var countCmd = connection.CreateConfiguredCommand();
            countCmd.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            countCmd.Parameters.AddWithValue("@RunID", runId);
            var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            await using var deleteCmd = connection.CreateConfiguredCommand();
            deleteCmd.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            deleteCmd.Parameters.AddWithValue("@RunID", runId);
            await deleteCmd.ExecuteNonQueryAsync();
            return count;
        }

        private async Task<bool> IsWorkspaceSavedAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM dbo.ValidationRuns WHERE RunID = @RunID
    AND (WorkspaceSavedAt IS NOT NULL OR EXISTS (
        SELECT 1 FROM dbo.ReviewSignoffs rs WHERE rs.RunID = ValidationRuns.RunID AND rs.SignoffRole = 'DataAnalyst'))
) THEN 1 ELSE 0 END;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }
    }
}
