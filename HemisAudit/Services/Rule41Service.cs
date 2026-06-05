using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using HemisAudit.Helpers;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule41Service : IRule41Service
    {
        private const int ExceptionRowSaveLimit  = 5000;
        private const int AgreeSampleLimit       = 100;
        private const int BrowserPreviewRowLimit = 50;

        private static readonly List<Rule41ColumnPair> DefaultPairs = new()
        {
            new Rule41ColumnPair { StudCol = "_007", AuditCol = "IAGSTNO", Label = "Student No"    },
            new Rule41ColumnPair { StudCol = "_008", AuditCol = "IADIDNO", Label = "Birth Date"    },
            new Rule41ColumnPair { StudCol = "_001", AuditCol = "IAGQUAL", Label = "Qualification" },
        };

        private readonly IConfiguration _configuration;

        public Rule41Service(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // ── Connection helpers ────────────────────────────────────────────────

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
            var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        // ── Table / column discovery ──────────────────────────────────────────

        public async Task<DatabaseListResult> GetDatabasesAsync(string server, string driver)
        {
            try
            {
                var connStr = BuildConnectionString(server, "master", driver);
                await using var conn = new SqlConnection(connStr);
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

        public async Task<Rule41TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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
                return new Rule41TableDiscoveryResult
                {
                    Success        = true,
                    Tables         = tables,
                    AutoStudTable  = FindFirst(tables, ["dbo_STUD", "STUD"], ["STUD"]),
                    AutoAuditTable = FindFirst(tables, ["MT-Audit-prod-std", "MT-Audit"], ["MT-Audit", "Audit"])
                };
            }
            catch (Exception ex) { return new Rule41TableDiscoveryResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule41ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName)
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
                return new Rule41ColumnDiscoveryResult
                {
                    Success = true,
                    Columns = cols,
                    AutoKey = FindFirst(cols, ["_007", "IAGSTNO"], ["_007", "IAGSTNO"])
                };
            }
            catch (Exception ex) { return new Rule41ColumnDiscoveryResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule41VerifyResult> VerifyTablesAsync(Rule41VerifyRequest request)
        {
            try
            {
                ValidateObjectName(request.StudTable);
                ValidateObjectName(request.AuditTable);
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                var st = Sanitise(request.StudTable);
                var at = Sanitise(request.AuditTable);
                return new Rule41VerifyResult
                {
                    Success    = true,
                    StudCount  = await CountAsync(conn, $"SELECT COUNT(*) FROM [{st}];"),
                    AuditCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{at}];")
                };
            }
            catch (Exception ex) { return new Rule41VerifyResult { Success = false, Error = ex.Message }; }
        }

        // ── Validation ────────────────────────────────────────────────────────

        public async Task<Rule41ValidationSummary> RunValidationAsync(Rule41ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ApplyDefaultPairs(request);
                ValidateRequest(request);

                var summary = await AnalyseAsync(request);

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
            catch (Exception ex) { return new Rule41ValidationSummary { Success = false, Error = ex.Message }; }
        }

        private static void ApplyDefaultPairs(Rule41ValidationRequest req)
        {
            if (req.Pairs == null || req.Pairs.Count == 0)
                req.Pairs = DefaultPairs.ToList();
        }

        private static void ApplyBrowserPreview(Rule41ValidationSummary? summary)
        {
            if (summary == null) return;
            TrimReconcRows(summary.Reconc);
        }

        private static void TrimReconcRows(Rule41ReconciliationSummary? reconc)
        {
            if (reconc == null) return;
            var excTake   = Math.Min(reconc.ExceptionRows.Count, BrowserPreviewRowLimit);
            var agreeTake = Math.Min(reconc.Rows.Count, Math.Max(0, BrowserPreviewRowLimit - excTake));
            reconc.ExceptionRows = reconc.ExceptionRows.Take(excTake).ToList();
            reconc.Rows          = reconc.Rows.Take(agreeTake).ToList();
        }

        private async Task<Rule41ValidationSummary> AnalyseAsync(Rule41ValidationRequest req)
        {
            var connStr = BuildConnectionString(req.Server, req.Database, req.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var studCols  = req.Pairs.Select(p => p.StudCol).Distinct().Union([req.StudKey]).ToList();
            var auditCols = req.Pairs.Select(p => p.AuditCol).Distinct().Union([req.AuditKey]).ToList();

            var studMap  = await LoadTableAsync(conn, req.StudTable,  req.StudKey,  studCols);
            var auditMap = await LoadTableAsync(conn, req.AuditTable, req.AuditKey, auditCols);

            var allKeys = studMap.Keys.Union(auditMap.Keys).ToList();
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            var reconc = RunReconciliation(studMap, req.StudKey, auditMap, req.AuditKey, allKeys, req.Pairs, req.StudTable, req.AuditTable);

            return new Rule41ValidationSummary
            {
                Success      = true,
                Timestamp    = now,
                Server       = req.Server,
                Database     = req.Database,
                StudTable    = req.StudTable,
                AuditTable   = req.AuditTable,
                StudKey      = req.StudKey,
                AuditKey     = req.AuditKey,
                TotalCount   = reconc.TotalCount,
                AgreeCount   = reconc.AgreeCount,
                DisagreeCount= reconc.DisagreeCount,
                MissingCount = reconc.MissingCount,
                ExceptionRate= reconc.ExceptionRate,
                ClientId     = req.ClientId,
                Status       = (reconc.DisagreeCount + reconc.MissingCount == 0) ? "PASS" : "FAIL",
                Reconc       = reconc
            };
        }

        private static Rule41ReconciliationSummary RunReconciliation(
            Dictionary<string, Dictionary<string, string?>> studMap,
            string studKey,
            Dictionary<string, Dictionary<string, string?>> auditMap,
            string auditKey,
            List<string> allNormKeys,
            List<Rule41ColumnPair> pairs,
            string studTable, string auditTable)
        {
            var reconc = new Rule41ReconciliationSummary
            {
                StudTable  = studTable,
                AuditTable = auditTable,
                StudKey    = studKey,
                AuditKey   = auditKey,
                Pairs      = pairs
            };

            int rowNo = 0;
            foreach (var normKey in allNormKeys.OrderBy(k => k))
            {
                rowNo++;
                var studRow  = studMap.GetValueOrDefault(normKey);
                var auditRow = auditMap.GetValueOrDefault(normKey);
                var rawRef   = studRow?.GetValueOrDefault(studKey) ?? auditRow?.GetValueOrDefault(auditKey) ?? normKey;
                var display  = Disp(rawRef);

                var row = new Rule41ReconcRow { RowNumber = rowNo, StudentRef = display };

                if (studRow == null)
                {
                    foreach (var p in pairs)
                        row.Fields[p.Label] = new Rule41FieldValue { AuditValue = Disp(auditRow?.GetValueOrDefault(p.AuditCol)), Match = "MISSING" };
                    row.OverallResult  = "MISSING";
                    row.DisagreeDetail = $"Student {display} not in {studTable}";
                }
                else if (auditRow == null)
                {
                    foreach (var p in pairs)
                        row.Fields[p.Label] = new Rule41FieldValue { StudValue = Disp(studRow.GetValueOrDefault(p.StudCol)), Match = "MISSING" };
                    row.OverallResult  = "MISSING";
                    row.DisagreeDetail = $"Student {display} not in {auditTable}";
                }
                else
                {
                    var diffs = new List<string>();
                    foreach (var p in pairs)
                    {
                        var sv    = Disp(studRow.GetValueOrDefault(p.StudCol));
                        var av    = Disp(auditRow.GetValueOrDefault(p.AuditCol));
                        var match = Norm(sv) == Norm(av) ? "AGREE" : "DISAGREE";
                        row.Fields[p.Label] = new Rule41FieldValue { StudValue = sv, AuditValue = av, Match = match };
                        if (match == "DISAGREE")
                            diffs.Add($"{p.Label}: STUD='{sv}' ≠ AUDIT='{av}'");
                    }
                    row.OverallResult  = diffs.Count == 0 ? "AGREE" : "DISAGREE";
                    row.DisagreeDetail = string.Join(" | ", diffs);
                }

                if (row.OverallResult != "AGREE")
                    reconc.ExceptionRows.Add(row);
                else
                    reconc.Rows.Add(row);
            }

            reconc.TotalCount    = rowNo;
            reconc.DisagreeCount = reconc.ExceptionRows.Count(r => r.OverallResult == "DISAGREE");
            reconc.MissingCount  = reconc.ExceptionRows.Count(r => r.OverallResult == "MISSING");
            reconc.AgreeCount    = reconc.TotalCount - reconc.ExceptionRows.Count;
            reconc.ExceptionRate = reconc.TotalCount == 0 ? 0m : Math.Round((decimal)reconc.ExceptionRows.Count / reconc.TotalCount * 100m, 2);

            reconc.ExceptionRows = reconc.ExceptionRows.Take(ExceptionRowSaveLimit).ToList();
            reconc.Rows          = reconc.Rows.Take(AgreeSampleLimit).ToList();

            return reconc;
        }

        // ── Table loading ─────────────────────────────────────────────────────

        private static async Task<Dictionary<string, Dictionary<string, string?>>> LoadTableAsync(
            SqlConnection conn, string tableName, string keyCol, List<string> columns)
        {
            ValidateObjectName(tableName);
            foreach (var c in columns) ValidateObjectName(c);

            var tbl     = Sanitise(tableName);
            var selCols = columns.Select(c => $"[{Sanitise(c)}]").ToList();
            if (!selCols.Any(c => c == $"[{Sanitise(keyCol)}]"))
                selCols.Insert(0, $"[{Sanitise(keyCol)}]");

            var sql = $"SELECT {string.Join(", ", selCols)} FROM [{tbl}];";
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandTimeout = SqlLargeDataExtensions.LargeDataCommandTimeoutSeconds;
            cmd.CommandText    = sql;
            await using var reader = await cmd.ExecuteReaderAsync();

            var fieldNames  = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
            var map         = new Dictionary<string, Dictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);
            var normKeyCol  = Sanitise(keyCol);

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++)
                    row[fieldNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString()?.Trim();

                var rawKey  = row.GetValueOrDefault(normKeyCol);
                var normKey = Norm(rawKey);
                if (!string.IsNullOrEmpty(normKey) && !map.ContainsKey(normKey))
                    map[normKey] = row;
            }
            return map;
        }

        // ── SQL generation ────────────────────────────────────────────────────

        public string GenerateSql(Rule41ValidationRequest request)
        {
            ApplyDefaultPairs(request);
            var st  = Sanitise(request.StudTable);
            var at  = Sanitise(request.AuditTable);
            var sk  = Sanitise(request.StudKey);
            var ak  = Sanitise(request.AuditKey);
            var pairs = request.Pairs ?? DefaultPairs;

            var fieldSel  = BuildFieldSelect(pairs, "s", "a");
            var agreeCond = BuildAgreeCondition(pairs, "s", "a");
            var testedCols = string.Join(", ", pairs.Select(p => $"{p.StudCol}↔{p.AuditCol}"));

            return $@"-- ============================================================
-- HEMIS RULE 41 – Student ASCII Agreement
-- Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}
-- Database  : {request.Database}
-- Tables    : [{st}] vs [{at}]
-- Join key  : [{sk}] ↔ [{ak}]
-- Columns   : {testedCols}
-- ============================================================

IF OBJECT_ID('tempdb..#Rule41') IS NOT NULL DROP TABLE #Rule41;

SELECT
    ROW_NUMBER() OVER (ORDER BY COALESCE(s.[{sk}], a.[{ak}])) AS Row_No,
    COALESCE(s.[{sk}], a.[{ak}]) AS Student_Ref,
{fieldSel},
    CASE
        WHEN s.[{sk}] IS NULL THEN 'MISSING-STUD'
        WHEN a.[{ak}] IS NULL THEN 'MISSING-AUDIT'
        WHEN {agreeCond} THEN 'AGREE'
        ELSE 'DISAGREE'
    END AS Overall_Result
INTO #Rule41
FROM [{st}] s
FULL OUTER JOIN [{at}] a
    ON UPPER(LTRIM(RTRIM(CAST(s.[{sk}] AS NVARCHAR(200)))))
     = UPPER(LTRIM(RTRIM(CAST(a.[{ak}] AS NVARCHAR(200)))));

-- Summary
SELECT
    COUNT(*) AS Total,
    SUM(CASE WHEN Overall_Result = 'AGREE'          THEN 1 ELSE 0 END) AS Agree,
    SUM(CASE WHEN Overall_Result = 'DISAGREE'        THEN 1 ELSE 0 END) AS Disagree,
    SUM(CASE WHEN Overall_Result LIKE 'MISSING%'    THEN 1 ELSE 0 END) AS Missing,
    CAST(SUM(CASE WHEN Overall_Result <> 'AGREE' THEN 1 ELSE 0 END) * 100.0 / NULLIF(COUNT(*),0) AS DECIMAL(5,2)) AS Exception_Pct
FROM #Rule41;

SELECT * FROM #Rule41 ORDER BY Row_No;
SELECT * FROM #Rule41 WHERE Overall_Result <> 'AGREE' ORDER BY Row_No;

DROP TABLE #Rule41;
-- ============================================================
-- END RULE 41
-- ============================================================".Trim();
        }

        private static string BuildFieldSelect(List<Rule41ColumnPair> pairs, string sAlias, string aAlias)
        {
            var lines = new List<string>();
            foreach (var p in pairs)
            {
                var sc = Sanitise(p.StudCol);
                var ac = Sanitise(p.AuditCol);
                lines.Add($"    {sAlias}.[{sc}] AS [STUD_{p.Label}],\n    {aAlias}.[{ac}] AS [AUDIT_{p.Label}],\n    CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(CAST({sAlias}.[{sc}] AS NVARCHAR(500)),''))))=UPPER(LTRIM(RTRIM(ISNULL(CAST({aAlias}.[{ac}] AS NVARCHAR(500)),'')))) THEN 'AGREE' ELSE 'DISAGREE' END AS [MATCH_{p.Label}]");
            }
            return string.Join(",\n", lines);
        }

        private static string BuildAgreeCondition(List<Rule41ColumnPair> pairs, string sAlias, string aAlias)
        {
            var conds = pairs.Select(p =>
            {
                var sc = Sanitise(p.StudCol);
                var ac = Sanitise(p.AuditCol);
                return $"UPPER(LTRIM(RTRIM(ISNULL(CAST({sAlias}.[{sc}] AS NVARCHAR(500)),''))))=UPPER(LTRIM(RTRIM(ISNULL(CAST({aAlias}.[{ac}] AS NVARCHAR(500)),''))))";
            });
            return string.Join("\n        AND ", conds);
        }

        // ── Save / Load ───────────────────────────────────────────────────────

        private async Task<int> SaveValidationRunAsync(Rule41ValidationRequest request, Rule41ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 41);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 41);
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
    @ClientID, @UserID, 41, 'Student ASCII Agreement', @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @AuditTable, @StudKey, @AuditKey,
    NULL, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);";
            command.Parameters.AddWithValue("@ClientID",    request.ClientId);
            command.Parameters.AddWithValue("@UserID",      systemUserId.Value);
            command.Parameters.AddWithValue("@Status",      summary.Status);
            command.Parameters.AddWithValue("@TotalRecords",summary.TotalCount);
            command.Parameters.AddWithValue("@PassCount",   summary.AgreeCount);
            command.Parameters.AddWithValue("@FailCount",   summary.DisagreeCount + summary.MissingCount);
            command.Parameters.AddWithValue("@ExceptionRate",summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase",request.Database);
            command.Parameters.AddWithValue("@StudTable",   request.StudTable);
            command.Parameters.AddWithValue("@AuditTable",  request.AuditTable);
            command.Parameters.AddWithValue("@StudKey",     request.StudKey);
            command.Parameters.AddWithValue("@AuditKey",    request.AuditKey);
            command.Parameters.AddWithValue("@ResultsJSON", json);
            command.Parameters.AddWithValue("@RunByUserName",(object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash",(object?)previousHash ?? DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCmd = connection.CreateConfiguredCommand();
            hashCmd.CommandText = "UPDATE dbo.ValidationRuns SET RecordHash = @RecordHash WHERE RunID = @RunID;";
            hashCmd.Parameters.AddWithValue("@RunID",      runId);
            hashCmd.Parameters.AddWithValue("@RecordHash", ComputeHash($"Rule41|{runId}|{request.ClientId}|{summary.Status}|{summary.TotalCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCmd.ExecuteNonQueryAsync();

            return runId;
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunInternalAsync(connection, runId);
        }

        public async Task<Rule41WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID, vr.ClientID,
    ISNULL(vr.HemisServer,'')              AS Server,
    ISNULL(vr.AuditDatabase,'')            AS [Database],
    ISNULL(vr.StudTable,'dbo_STUD')        AS StudTable,
    ISNULL(vr.DeceasedTable,'MT-Audit-prod-std') AS AuditTable,
    ISNULL(vr.StudColumn,'_007')           AS StudKey,
    ISNULL(vr.DeceasedColumn,'IAGSTNO')    AS AuditKey,
    ISNULL(vr.Status,'')                   AS Status,
    vr.LastEditedByUserName, vr.LastEditedAt, vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = 41 AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var summary = DeserializeSummary(reader.IsDBNull(11) ? null : reader.GetString(11));
            ApplyBrowserPreview(summary);

            var workspace = new Rule41WorkspaceStateViewModel
            {
                ClientId             = reader.GetInt32(1),
                RunId                = reader.GetInt32(0),
                Server               = reader.IsDBNull(2)  ? "" : reader.GetString(2),
                Database             = reader.IsDBNull(3)  ? "" : reader.GetString(3),
                StudTable            = reader.IsDBNull(4)  ? "dbo_STUD"          : reader.GetString(4),
                AuditTable           = reader.IsDBNull(5)  ? "MT-Audit-prod-std" : reader.GetString(5),
                StudKey              = reader.IsDBNull(6)  ? "_007"              : reader.GetString(6),
                AuditKey             = reader.IsDBNull(7)  ? "IAGSTNO"           : reader.GetString(7),
                CurrentStatus        = reader.IsDBNull(8)  ? "" : reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9)  ? null : reader.GetString(9),
                LastEditedAt         = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Driver               = "ODBC Driver 17 for SQL Server",
                Summary              = summary
            };

            await reader.CloseAsync();

            workspace.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId!.Value, currentUserId);
            workspace.HasDataAnalystSignoff   = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            var roleSignoff = signoffs.FirstOrDefault(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff    = roleSignoff != null;
            workspace.CurrentUserSignoffComment  = roleSignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved           = await IsWorkspaceSavedAsync(connection, workspace.RunId!.Value);

            return workspace;
        }

        public async Task<Rule41RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID AND vr.RuleNumber = 41;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null) return null;

            var viewModel = new Rule41RunReviewViewModel
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
            viewModel.CanCurrentUserSignOff = ValidationRunAccessPolicy.CanCompleteReviewSignoff(viewModel.CurrentUserEngagementRole, viewModel.CurrentUserEngagementRole, viewModel.HasDataAnalystSignoff);

            return viewModel;
        }

        public async Task<Rule41WorkspaceSaveResult> SaveWorkspaceAsync(Rule41ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                    return new Rule41WorkspaceSaveResult { Success = false, Error = "Run the validation first." };

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunInternalAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                    return new Rule41WorkspaceSaveResult { Success = false, Error = "The saved run could not be found for this engagement." };

                await EnsureClientNotArchivedAsync(connection, request.ClientId);
                await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 41);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                var previousHash    = await GetValidationRecordHashAsync(connection, request.RunId.Value);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer = @Server, AuditDatabase = @Database, StudTable = @StudTable,
    DeceasedTable = @AuditTable, StudColumn = @StudKey, DeceasedColumn = @AuditKey,
    LastEditedByUserName = @LastEditedByUserName, LastEditedAt = GETDATE(), WorkspaceSavedAt = GETDATE(),
    PreviousHash = @PreviousHash, RecordHash = @RecordHash, Status = 'Needs Review', IsCurrent = 1
WHERE RunID = @RunID AND ClientID = @ClientID;";
                command.Parameters.AddWithValue("@RunID",               request.RunId.Value);
                command.Parameters.AddWithValue("@ClientID",            request.ClientId);
                command.Parameters.AddWithValue("@Server",              request.Server);
                command.Parameters.AddWithValue("@Database",            request.Database);
                command.Parameters.AddWithValue("@StudTable",           request.StudTable);
                command.Parameters.AddWithValue("@AuditTable",          request.AuditTable);
                command.Parameters.AddWithValue("@StudKey",             request.StudKey);
                command.Parameters.AddWithValue("@AuditKey",            request.AuditKey);
                command.Parameters.AddWithValue("@LastEditedByUserName",(object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash",        (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash",          ComputeHash($"WorkspaceSave|Rule41|{request.RunId.Value}|{request.ClientId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail);
                return new Rule41WorkspaceSaveResult
                {
                    Success             = true,
                    Message             = clearedSignoffs > 0 ? "Workspace saved. Existing signoffs were removed." : "Workspace saved.",
                    SignoffsCleared     = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace           = workspace
                };
            }
            catch (Exception ex) { return new Rule41WorkspaceSaveResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule41WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunInternalAsync(connection, runId);
                if (!clientId.HasValue)
                    return new Rule41WorkspaceSaveResult { Success = false, Error = "Saved workspace was not found." };

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                await MarkPreviousRunsHistoricalAsync(connection, clientId.Value, 41);

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
                markEdit.Parameters.AddWithValue("@RecordHash",         ComputeHash($"BeginEdit|Rule41|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail);
                return new Rule41WorkspaceSaveResult
                {
                    Success             = true,
                    Message             = clearedSignoffs > 0 ? "Editing has begun. Signoffs were removed." : "Editing has begun.",
                    SignoffsCleared     = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace           = workspace
                };
            }
            catch (Exception ex) { return new Rule41WorkspaceSaveResult { Success = false, Error = ex.Message }; }
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
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

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

        // ── System DB helpers ─────────────────────────────────────────────────

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
       CASE WHEN @CurrentUserID IS NOT NULL AND rs.ReviewerID = @CurrentUserID THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsCurrentUser
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.Users u ON u.UserID = rs.ReviewerID
WHERE rs.RunID = @RunID
ORDER BY CASE ISNULL(rs.SignoffRole,'') WHEN 'DataAnalyst' THEN 1 WHEN 'Manager' THEN 2 WHEN 'Director' THEN 3 ELSE 4 END, rs.SignedOffAt DESC;";
            command.Parameters.AddWithValue("@RunID",        runId);
            command.Parameters.AddWithValue("@CurrentUserID",currentUserId.HasValue ? currentUserId.Value : DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync();
            var signoffs = new List<RunSignoffViewModel>();
            while (await reader.ReadAsync())
            {
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
            }
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

        // ── Utilities ─────────────────────────────────────────────────────────

        private static string Norm(string? v) =>
            string.IsNullOrWhiteSpace(v) ? "" : System.Text.RegularExpressions.Regex.Replace(v.Trim().ToUpperInvariant(), @"\s+", " ");

        private static string Disp(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v.Trim();

        private static string Sanitise(string name) =>
            name.Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();

        private static void ValidateObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Table or column name is required.");
            foreach (var bad in new[] { ";", "'", "\"", "--", "/*", "*/" })
                if (value.Contains(bad, StringComparison.Ordinal))
                    throw new InvalidOperationException("Unsafe table or column name was provided.");
        }

        private static void ValidateRequest(Rule41ValidationRequest r)
        {
            ValidateObjectName(r.StudTable);
            ValidateObjectName(r.AuditTable);
            ValidateObjectName(r.StudKey);
            ValidateObjectName(r.AuditKey);
            foreach (var p in r.Pairs) { ValidateObjectName(p.StudCol); ValidateObjectName(p.AuditCol); }
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

        private static Rule41ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonConvert.DeserializeObject<Rule41ValidationSummary>(ValidationPayloadCodec.Decode(json)); }
            catch { return null; }
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager",     StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director",    StringComparison.OrdinalIgnoreCase);
    }
}
