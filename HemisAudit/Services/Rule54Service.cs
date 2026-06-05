using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule54Service : IRule54Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule54Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
        {
            _configuration = configuration;
            _pendingValidationCache = pendingValidationCache;
        }

        private static string BuildConnectionString(string server, string database, string driver) =>
            $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";

        private static string NormName(string? v)
        {
            if (v == null) return "";
            return Regex.Replace(v.Trim().ToUpperInvariant(), @"\s+", " ");
        }

        private static string NormValue(string? v) =>
            v == null ? "" : v.Trim().ToUpperInvariant();

        // ── Internal record types ─────────────────────────────────────────────

        private record HemisRecord(string RecordId, string QualRecordId, string CourseCode, string CreditValue, string Research1, string QualName);
        private record PqmRow(string? QualName, string? Research1);

        // ── Core validation ───────────────────────────────────────────────────

        private static Rule54ValidationRow ValidateRecord(int rowNo, HemisRecord h, List<PqmRow> pqm)
        {
            var hNorm = NormName(h.QualName);

            var nameRows = pqm
                .Where(p => string.Equals(NormName(p.QualName), hNorm, StringComparison.Ordinal))
                .ToList();

            if (nameRows.Count == 0)
            {
                return new Rule54ValidationRow
                {
                    ValidationNumber = rowNo,
                    RecordId         = h.RecordId,
                    QualRecordId     = h.QualRecordId,
                    CourseCode       = h.CourseCode,
                    CreditValue      = h.CreditValue,
                    HemisResearch1   = h.Research1,
                    HemisQualName    = h.QualName,
                    PqmQualName      = null,
                    PqmResearch1     = null,
                    QualNameMatch    = false,
                    Research1Match   = false,
                    ValidationResult = "FAIL",
                    ExceptionReason  = $"Qualification name '{h.QualName}' not found in PQM (Authorised_Qualification_Name)"
                };
            }

            var research1Norm = NormValue(h.Research1);
            var combined = nameRows
                .Where(p => string.Equals(NormValue(p.Research1), research1Norm, StringComparison.Ordinal))
                .ToList();

            if (combined.Count > 0)
            {
                var best = combined[0];
                return new Rule54ValidationRow
                {
                    ValidationNumber = rowNo,
                    RecordId         = h.RecordId,
                    QualRecordId     = h.QualRecordId,
                    CourseCode       = h.CourseCode,
                    CreditValue      = h.CreditValue,
                    HemisResearch1   = h.Research1,
                    HemisQualName    = h.QualName,
                    PqmQualName      = best.QualName?.Trim(),
                    PqmResearch1     = best.Research1?.Trim(),
                    QualNameMatch    = true,
                    Research1Match   = true,
                    ValidationResult = "PASS",
                    ExceptionReason  = null
                };
            }

            // Name matched, Research_1 did not
            var bestName = nameRows[0];
            var pqmResearch1Values = string.Join(" | ",
                nameRows.Take(3)
                        .Select(p => p.Research1?.Trim())
                        .Where(v => v != null)
                        .Distinct());

            return new Rule54ValidationRow
            {
                ValidationNumber = rowNo,
                RecordId         = h.RecordId,
                QualRecordId     = h.QualRecordId,
                CourseCode       = h.CourseCode,
                CreditValue      = h.CreditValue,
                HemisResearch1   = h.Research1,
                HemisQualName    = h.QualName,
                PqmQualName      = bestName.QualName?.Trim(),
                PqmResearch1     = bestName.Research1?.Trim(),
                QualNameMatch    = true,
                Research1Match   = false,
                ValidationResult = "FAIL",
                ExceptionReason  = $"Name matched ('{h.QualName}') but Research_1 mismatch — " +
                                   $"HEMIS _050: '{h.Research1}' | PQM Research_1: '{pqmResearch1Values}'"
            };
        }

        // ── Public API ────────────────────────────────────────────────────────

        public async Task<DatabaseListResult> GetDatabasesAsync(string server, string driver)
        {
            try
            {
                var connStr = $"Server={server};Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT name FROM sys.databases " +
                    "WHERE name NOT IN ('master','tempdb','model','msdb') ORDER BY name", conn)
                    .WithLargeDataTimeout();
                using var reader = await cmd.ExecuteReaderAsync();
                var dbs = new List<string>();
                while (await reader.ReadAsync())
                    dbs.Add(reader.GetString(0));
                return new DatabaseListResult { Success = true, Databases = dbs };
            }
            catch (Exception ex)
            {
                return new DatabaseListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule54TableListResult> GetTablesAsync(string server, string database, string driver)
        {
            try
            {
                var connStr = BuildConnectionString(server, database, driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES " +
                    "WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME", conn)
                    .WithLargeDataTimeout();
                using var reader = await cmd.ExecuteReaderAsync();
                var tables = new List<string>();
                while (await reader.ReadAsync())
                    tables.Add(reader.GetString(0));

                return new Rule54TableListResult
                {
                    Success = true,
                    Tables = tables,
                    AutoCredTable = tables.FirstOrDefault(t =>
                        t.Equals("dbo_CRED", StringComparison.OrdinalIgnoreCase)),
                    AutoQualTable = tables.FirstOrDefault(t =>
                        t.Equals("dbo_QUAL", StringComparison.OrdinalIgnoreCase)),
                    AutoPqmTable = tables.FirstOrDefault(t =>
                        t.Equals("PQM", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("PQM", StringComparison.OrdinalIgnoreCase))
                };
            }
            catch (Exception ex)
            {
                return new Rule54TableListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<ColumnListResult> GetColumnsAsync(
            string server, string database, string driver, string tableName, string tableRole)
        {
            try
            {
                var connStr = BuildConnectionString(server, database, driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
                    "WHERE TABLE_NAME=@t ORDER BY ORDINAL_POSITION", conn)
                    .WithLargeDataTimeout();
                cmd.Parameters.AddWithValue("@t", tableName);
                using var reader = await cmd.ExecuteReaderAsync();
                var columns = new List<string>();
                while (await reader.ReadAsync())
                    columns.Add(reader.GetString(0));

                string? autoSelected = tableRole?.ToLowerInvariant() switch
                {
                    "cred_id"       => columns.FirstOrDefault(c => c.Equals("_001", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "cred_course"   => columns.FirstOrDefault(c => c.Equals("_030", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "cred_credit"   => columns.FirstOrDefault(c => c.Equals("_036", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "cred_research1"=> columns.FirstOrDefault(c => c.Equals("_050", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_id"       => columns.FirstOrDefault(c => c.Equals("_001", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_name"     => columns.FirstOrDefault(c => c.Equals("_003", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_name"      => columns.FirstOrDefault(c => c.Contains("Authorised", StringComparison.OrdinalIgnoreCase) || c.Contains("Qualification_Name", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_research1" => columns.FirstOrDefault(c => c.Equals("Research_1", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    _               => columns.FirstOrDefault()
                };

                return new ColumnListResult
                {
                    Success = true,
                    Columns = columns,
                    AutoSelected = autoSelected
                };
            }
            catch (Exception ex)
            {
                return new ColumnListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule54VerifyResult> VerifyDataAsync(Rule54VerifyRequest request)
        {
            try
            {
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var ct = Sanitise(request.CredTable);
                var qt = Sanitise(request.QualTable);
                var pt = Sanitise(request.PqmTable);
                var ci = Sanitise(request.CredIdCol);
                var qi = Sanitise(request.QualIdCol);

                var sql = $@"
SELECT
    (SELECT COUNT(*) FROM [{ct}]) AS CRED_Total,
    (SELECT COUNT(*) FROM [{qt}]) AS QUAL_Total,
    (SELECT COUNT(*) FROM [{pt}]) AS PQM_Total,
    (SELECT COUNT(*) FROM [{ct}] c INNER JOIN [{qt}] q ON c.[{ci}] = q.[{qi}]) AS Merged_Total";

                using var cmd = new SqlCommand(sql, conn).WithLargeDataTimeout();
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new Rule54VerifyResult
                    {
                        Success     = true,
                        CredTotal   = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
                        QualTotal   = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                        PqmTotal    = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2)),
                        MergedTotal = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3))
                    };
                }
                return new Rule54VerifyResult { Success = false, Error = "No data returned" };
            }
            catch (Exception ex)
            {
                return new Rule54VerifyResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule54ValidationSummary> RunValidationAsync(
            Rule54ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var ct  = Sanitise(request.CredTable);
                var qt  = Sanitise(request.QualTable);
                var pt  = Sanitise(request.PqmTable);
                var ci  = Sanitise(request.CredIdCol);
                var cc  = Sanitise(request.CredCourseCol);
                var cv  = Sanitise(request.CredCreditCol);
                var cr1 = Sanitise(request.CredResearch1Col);
                var qi  = Sanitise(request.QualIdCol);
                var qn  = Sanitise(request.QualNameCol);
                var pn  = Sanitise(request.PqmNameCol);
                var pr1 = Sanitise(request.PqmResearch1Col);

                // Load CRED ⋈ QUAL
                var hemis = new List<HemisRecord>();
                using (var cmd = new SqlCommand(
                    $"SELECT c.[{ci}], q.[{qi}], c.[{cc}], c.[{cv}], c.[{cr1}], q.[{qn}] " +
                    $"FROM [{ct}] c INNER JOIN [{qt}] q ON c.[{ci}] = q.[{qi}]", conn)
                    .WithLargeDataTimeout())
                using (var r = await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        hemis.Add(new HemisRecord(
                            r.IsDBNull(0) ? "" : r.GetValue(0)?.ToString() ?? "",
                            r.IsDBNull(1) ? "" : r.GetValue(1)?.ToString() ?? "",
                            r.IsDBNull(2) ? "" : r.GetValue(2)?.ToString() ?? "",
                            r.IsDBNull(3) ? "" : r.GetValue(3)?.ToString() ?? "",
                            r.IsDBNull(4) ? "" : r.GetValue(4)?.ToString() ?? "",
                            r.IsDBNull(5) ? "" : r.GetValue(5)?.ToString() ?? ""));
                    }
                }

                // Load PQM
                var pqm = new List<PqmRow>();
                using (var cmd = new SqlCommand(
                    $"SELECT [{pn}], [{pr1}] FROM [{pt}]", conn).WithLargeDataTimeout())
                using (var r = await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        pqm.Add(new PqmRow(
                            r.IsDBNull(0) ? null : r.GetValue(0)?.ToString(),
                            r.IsDBNull(1) ? null : r.GetValue(1)?.ToString()));
                    }
                }

                // Validate in memory
                var validationRows = hemis
                    .Select((h, idx) => ValidateRecord(idx + 1, h, pqm))
                    .ToList();

                var total     = validationRows.Count;
                var passCount = validationRows.Count(r => r.ValidationResult == "PASS");
                var failCount = validationRows.Count(r => r.ValidationResult == "FAIL");
                var rate      = total > 0 ? Math.Round((decimal)failCount / total * 100, 2) : 0;

                var exceptions = validationRows
                    .Where(r => r.ValidationResult == "FAIL")
                    .Select(r => new Rule54ExceptionRecord
                    {
                        ValidationNumber = r.ValidationNumber,
                        RecordId         = r.RecordId,
                        QualRecordId     = r.QualRecordId,
                        CourseCode       = r.CourseCode,
                        CreditValue      = r.CreditValue,
                        HemisResearch1   = r.HemisResearch1,
                        HemisQualName    = r.HemisQualName,
                        PqmQualName      = r.PqmQualName,
                        PqmResearch1     = r.PqmResearch1,
                        QualNameMatch    = r.QualNameMatch,
                        Research1Match   = r.Research1Match,
                        ValidationResult = r.ValidationResult,
                        ExceptionReason  = r.ExceptionReason ?? ""
                    })
                    .ToList();

                var summary = new Rule54ValidationSummary
                {
                    Success          = true,
                    TotalValidated   = total,
                    PassCount        = passCount,
                    FailCount        = failCount,
                    ExceptionRate    = rate,
                    Status           = failCount == 0 ? "PASS" : "FAIL",
                    Timestamp        = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Database         = request.Database,
                    CredTable        = request.CredTable,
                    QualTable        = request.QualTable,
                    PqmTable         = request.PqmTable,
                    CredIdCol        = request.CredIdCol,
                    CredCourseCol    = request.CredCourseCol,
                    CredCreditCol    = request.CredCreditCol,
                    CredResearch1Col = request.CredResearch1Col,
                    QualIdCol        = request.QualIdCol,
                    QualNameCol      = request.QualNameCol,
                    PqmNameCol       = request.PqmNameCol,
                    PqmResearch1Col  = request.PqmResearch1Col,
                    ClientId         = request.ClientId,
                    ValidationRows   = validationRows,
                    Exceptions       = exceptions
                };

                if (request.ClientId > 0)
                {
                    await using var systemConnection = await OpenSystemConnectionAsync();
                    var systemUserId = await GetSystemUserIdByEmailAsync(systemConnection, userEmail);
                    if (!systemUserId.HasValue)
                        throw new InvalidOperationException("Current user is not available in the system database.");

                    await EnsureClientNotArchivedAsync(systemConnection, request.ClientId);
                    await ClearRuleSignoffsAsync(systemConnection, request.ClientId, 54);
                    await MarkPreviousRunsHistoricalAsync(systemConnection, request.ClientId, 54);

                    var runId = await InsertValidationRunAsync(systemConnection, request, summary, systemUserId.Value, userName);
                    summary.SavedRunId = runId;

                    await using var update = systemConnection.CreateConfiguredCommand();
                    update.CommandText = @"
UPDATE dbo.ValidationRuns
SET ResultsJSON = @ResultsJSON
WHERE RunID = @RunID;";
                    update.Parameters.AddWithValue("@RunID", runId);
                    update.Parameters.AddWithValue("@ResultsJSON",
                        ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
                    await update.ExecuteNonQueryAsync();
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule54ValidationSummary { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule54WorkspaceSaveResult> SaveWorkspaceAsync(
            Rule54ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                    return new Rule54WorkspaceSaveResult { Success = false, Error = "Run validation before saving the workspace." };

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                    return new Rule54WorkspaceSaveResult { Success = false, Error = "The saved workspace could not be found for this engagement." };

                await EnsureClientNotArchivedAsync(connection, request.ClientId);

                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                    return new Rule54WorkspaceSaveResult { Success = false, Error = "Current user is not available in the system database." };

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                var previousHash    = await GetValidationRecordHashAsync(connection, request.RunId.Value);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer          = @HemisServer,
    AuditDatabase        = @AuditDatabase,
    StudTable            = @StudTable,
    DeceasedTable        = @DeceasedTable,
    StudColumn           = @StudColumn,
    DeceasedColumn       = @DeceasedColumn,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    WorkspaceSavedAt     = GETDATE(),
    PreviousHash         = @PreviousHash,
    RecordHash           = @RecordHash,
    Status               = 'Needs Review'
WHERE RunID   = @RunID
  AND ClientID = @ClientID;";
                command.Parameters.AddWithValue("@RunID",               request.RunId.Value);
                command.Parameters.AddWithValue("@ClientID",            request.ClientId);
                command.Parameters.AddWithValue("@HemisServer",         request.Server);
                command.Parameters.AddWithValue("@AuditDatabase",       request.Database);
                command.Parameters.AddWithValue("@StudTable",           request.CredTable);
                command.Parameters.AddWithValue("@DeceasedTable",       request.QualTable);
                command.Parameters.AddWithValue("@StudColumn",          request.PqmTable);
                command.Parameters.AddWithValue("@DeceasedColumn",      "");
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash",        (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash(
                    $"WorkspaceSave|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|" +
                    $"{request.CredTable}|{request.QualTable}|{request.PqmTable}|" +
                    $"{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail);
                return new Rule54WorkspaceSaveResult
                {
                    Success = true,
                    Message = clearedSignoffs > 0
                        ? "Workspace saved. Existing signoffs were removed and the run must be reviewed again."
                        : "Workspace saved and marked for review again.",
                    SignoffsCleared     = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace           = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule54WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule54WorkspaceSaveResult> BeginWorkspaceEditAsync(
            int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                    return new Rule54WorkspaceSaveResult { Success = false, Error = "Current user is not available in the system database." };

                var clientId = await GetClientIdForRunAsync(runId);
                if (!clientId.HasValue)
                    return new Rule54WorkspaceSaveResult { Success = false, Error = "Saved workspace was not found." };

                await EnsureClientNotArchivedAsync(connection, clientId.Value);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash    = await GetValidationRecordHashAsync(connection, runId);

                await using (var markEdit = connection.CreateConfiguredCommand())
                {
                    markEdit.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    WorkspaceSavedAt     = NULL,
    PreviousHash         = @PreviousHash,
    RecordHash           = @RecordHash,
    Status               = 'Needs Review'
WHERE RunID = @RunID;";
                    markEdit.Parameters.AddWithValue("@RunID", runId);
                    markEdit.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? reviewerEmail);
                    markEdit.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                    markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash(
                        $"WorkspaceEdit|{runId}|{reviewerName ?? reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                    await markEdit.ExecuteNonQueryAsync();
                }

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail);
                return new Rule54WorkspaceSaveResult
                {
                    Success             = true,
                    Message             = clearedSignoffs > 0
                        ? "Editing has begun. Existing signoffs were removed so the workspace must be reviewed again."
                        : "Editing has begun.",
                    SignoffsCleared     = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace           = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule54WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
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

        public async Task<Rule54WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(
            int clientId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID,
    vr.ClientID,
    ISNULL(vr.HemisServer,   '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable,     '') AS CredTable,
    ISNULL(vr.DeceasedTable, '') AS QualTable,
    ISNULL(vr.StudColumn,    '') AS PqmTable,
    ISNULL(vr.Status,        '') AS Status,
    ISNULL(vr.LastEditedByUserName, '') AS LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID   = @ClientID
  AND vr.RuleNumber = 54
ORDER BY vr.IsCurrent DESC, vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summaryJson = reader.IsDBNull(10) ? null : reader.GetString(10);
            var summary = DeserializeSummary(summaryJson);
            if (summary != null)
                ApplyBrowserPreview(summary);

            var workspace = new Rule54WorkspaceStateViewModel
            {
                RunId         = reader.GetInt32(0),
                ClientId      = reader.GetInt32(1),
                Server        = reader.GetString(2),
                Database      = reader.GetString(3),
                Driver        = "ODBC Driver 17 for SQL Server",
                CredTable     = reader.GetString(4),
                QualTable     = reader.GetString(5),
                PqmTable      = reader.GetString(6),
                CurrentStatus = reader.GetString(7),
                LastEditedByUserName = reader.IsDBNull(8) ? null : reader.GetString(8),
                LastEditedAt         = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                Summary              = summary
            };

            if (summary != null)
            {
                workspace.CredIdCol        = summary.CredIdCol;
                workspace.CredCourseCol    = summary.CredCourseCol;
                workspace.CredCreditCol    = summary.CredCreditCol;
                workspace.CredResearch1Col = summary.CredResearch1Col;
                workspace.QualIdCol        = summary.QualIdCol;
                workspace.QualNameCol      = summary.QualNameCol;
                workspace.PqmNameCol       = summary.PqmNameCol;
                workspace.PqmResearch1Col  = summary.PqmResearch1Col;
            }

            await reader.CloseAsync();

            int? currentUserId = null;
            if (!string.IsNullOrWhiteSpace(currentUserEmail))
            {
                currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);
                if (currentUserId.HasValue)
                    workspace.CurrentUserEngagementRole =
                        await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? "";
            }

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId!.Value, currentUserId);
            workspace.HasDataAnalystSignoff = signoffs.Any(s =>
                string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            var currentRoleSignoff = signoffs.FirstOrDefault(s =>
                HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(
                    s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff   = currentRoleSignoff != null;
            workspace.CurrentUserSignoffComment = currentRoleSignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved          = await IsWorkspaceSavedAsync(connection, workspace.RunId!.Value);

            if (workspace.Summary != null)
                workspace.Summary.SavedRunId = workspace.RunId;

            if (string.IsNullOrWhiteSpace(workspace.CurrentStatus))
                workspace.CurrentStatus = workspace.Summary?.Status ?? "";

            return workspace;
        }

        public async Task<Rule54RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summaryJson = reader.IsDBNull(6) ? null : reader.GetString(6);
            var summary = DeserializeSummary(summaryJson);
            if (summary == null)
                return null;

            var viewModel = new Rule54RunReviewViewModel
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

            int? currentUserId = null;
            if (!string.IsNullOrWhiteSpace(currentUserEmail))
            {
                currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);
                if (currentUserId.HasValue)
                    viewModel.CurrentUserEngagementRole =
                        await GetEngagementRoleAsync(connection, viewModel.ClientId, currentUserId.Value) ?? "";
            }

            viewModel.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            viewModel.HasDataAnalystSignoff = viewModel.Signoffs.Any(s =>
                string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            return viewModel;
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue)
                throw new InvalidOperationException("Reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("Validation run was not found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(engagementRole))
                throw new InvalidOperationException("Only assigned data analysts, managers, and directors can sign off a validation run.");

            if (!string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) &&
                !await HasSignoffRoleAsync(connection, runId, "DataAnalyst"))
            {
                throw new InvalidOperationException("The assigned data analyst must sign off before this review can be completed.");
            }

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND ReviewerID = @ReviewerID)
BEGIN
    UPDATE dbo.ReviewSignoffs
    SET SignoffRole = @SignoffRole, ReviewType = 'Final', Comment = @Comment, SignedOffAt = GETDATE()
    WHERE RunID = @RunID AND ReviewerID = @ReviewerID;
END
ELSE
BEGIN
    INSERT INTO dbo.ReviewSignoffs (ClientID, RunID, ReviewerID, SignoffRole, ReviewType, Comment, SignedOffAt)
    VALUES (@ClientID, @RunID, @ReviewerID, @SignoffRole, 'Final', @Comment, GETDATE());
END";
            command.Parameters.AddWithValue("@ClientID",    clientId.Value);
            command.Parameters.AddWithValue("@RunID",       runId);
            command.Parameters.AddWithValue("@ReviewerID",  reviewerId.Value);
            command.Parameters.AddWithValue("@SignoffRole", engagementRole!);
            command.Parameters.AddWithValue("@Comment",
                string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim());
            await command.ExecuteNonQueryAsync();

            await UpdateRunStatusFromSignoffsAsync(connection, runId);
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue)
                throw new InvalidOperationException("Reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("Validation run was not found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(
                connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0) return;
        }

        public string GenerateSql(Rule54ValidationRequest request)
        {
            var ct  = request.CredTable;
            var qt  = request.QualTable;
            var pt  = request.PqmTable;
            var ci  = request.CredIdCol;
            var cc  = request.CredCourseCol;
            var cv  = request.CredCreditCol;
            var cr1 = request.CredResearch1Col;
            var qi  = request.QualIdCol;
            var qn  = request.QualNameCol;
            var pn  = request.PqmNameCol;
            var pr1 = request.PqmResearch1Col;

            return $@"-- ============================================================================
-- HEMIS 2025 - RULE 54: CRED vs QUAL vs PQM VALIDATION
-- Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}
-- ============================================================================
-- TABLES
--   {ct}  : [{ci}] (record ID / join key), [{cc}] (course code), [{cv}] (credit value), [{cr1}] (Research_1)
--   {qt}  : [{qi}] (record ID / join key), [{qn}] (qualification name)
--   {pt}  : [{pn}] (Authorised_Qualification_Name), [{pr1}] (Research_1)
--
-- JOIN:   {ct}.[{ci}] = {qt}.[{qi}]
--
-- MATCHING RULES (both on the SAME PQM row):
--   ① UPPER(TRIM([{qn}])) = UPPER(TRIM([{pn}]))   -- qualification name match
--   ② TRIM([{cr1}])       = TRIM([{pr1}])          -- Research_1 value match
-- ============================================================================

IF OBJECT_ID('tempdb..#R54_Base') IS NOT NULL DROP TABLE #R54_Base;
IF OBJECT_ID('tempdb..#R54_Val')  IS NOT NULL DROP TABLE #R54_Val;

-- Step 1: CRED ⋈ QUAL → per-row dataset
SELECT
    c.[{ci}]  AS Record_ID,
    c.[{cc}]  AS Course_Code,
    c.[{cv}]  AS Credit_Value,
    c.[{cr1}] AS HEMIS_Research_1,
    q.[{qn}]  AS HEMIS_Qual_Name
INTO #R54_Base
FROM [{ct}] c
INNER JOIN [{qt}] q ON c.[{ci}] = q.[{qi}];

-- Step 2: Validate each HEMIS record against PQM
SELECT
    ROW_NUMBER() OVER (ORDER BY b.Record_ID)   AS Validation_Number,
    b.Record_ID,
    b.Course_Code,
    b.Credit_Value,
    b.HEMIS_Research_1,
    b.HEMIS_Qual_Name,

    -- PQM Authorised_Qualification_Name (first name-matched row)
    (SELECT TOP 1 CAST(p.[{pn}] AS NVARCHAR(500))
     FROM [{pt}] p
     WHERE UPPER(LTRIM(RTRIM(CAST(b.HEMIS_Qual_Name AS NVARCHAR(500)))))
           = UPPER(LTRIM(RTRIM(CAST(p.[{pn}] AS NVARCHAR(500)))))
    )                                           AS PQM_Qual_Name,

    -- PQM Research_1 (from first name-matched row)
    (SELECT TOP 1 CAST(p.[{pr1}] AS NVARCHAR(50))
     FROM [{pt}] p
     WHERE UPPER(LTRIM(RTRIM(CAST(b.HEMIS_Qual_Name AS NVARCHAR(500)))))
           = UPPER(LTRIM(RTRIM(CAST(p.[{pn}] AS NVARCHAR(500)))))
    )                                           AS PQM_Research_1,

    -- Qual name match flag
    CASE WHEN EXISTS (
        SELECT 1 FROM [{pt}] p
        WHERE UPPER(LTRIM(RTRIM(CAST(b.HEMIS_Qual_Name AS NVARCHAR(500)))))
              = UPPER(LTRIM(RTRIM(CAST(p.[{pn}] AS NVARCHAR(500)))))
    ) THEN 'YES' ELSE 'NO' END                  AS Qual_Name_Match,

    -- Combined match flag (BOTH on same row)
    CASE WHEN EXISTS (
        SELECT 1 FROM [{pt}] p
        WHERE UPPER(LTRIM(RTRIM(CAST(b.HEMIS_Qual_Name    AS NVARCHAR(500)))))
              = UPPER(LTRIM(RTRIM(CAST(p.[{pn}]           AS NVARCHAR(500)))))
          AND LTRIM(RTRIM(CAST(b.HEMIS_Research_1 AS NVARCHAR(50))))
              = LTRIM(RTRIM(CAST(p.[{pr1}]        AS NVARCHAR(50))))
    ) THEN 'PASS' ELSE 'FAIL' END               AS Validation_Result

INTO #R54_Val
FROM #R54_Base b;

-- Step 3: Summary
SELECT
    COUNT(*)                                                             AS Total,
    SUM(CASE WHEN Validation_Result='PASS' THEN 1 ELSE 0 END)           AS Pass_Count,
    SUM(CASE WHEN Validation_Result='FAIL' THEN 1 ELSE 0 END)           AS Fail_Count,
    CAST(SUM(CASE WHEN Validation_Result='FAIL' THEN 1 ELSE 0 END)
         * 100.0 / NULLIF(COUNT(*),0) AS DECIMAL(5,2))                  AS Exception_Rate_Pct
FROM #R54_Val;

-- Step 4: Full result
SELECT Validation_Number, Record_ID, Course_Code, Credit_Value,
       HEMIS_Research_1, HEMIS_Qual_Name,
       PQM_Qual_Name, PQM_Research_1,
       Qual_Name_Match, Validation_Result
FROM #R54_Val ORDER BY Validation_Number;

-- Step 5: Exceptions only
SELECT * FROM #R54_Val WHERE Validation_Result='FAIL' ORDER BY Validation_Number;

DROP TABLE #R54_Base; DROP TABLE #R54_Val;
-- ============================================================================
-- END OF RULE 54 CRED vs QUAL vs PQM VALIDATION
-- ============================================================================
";
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private async Task<int> InsertValidationRunAsync(
            SqlConnection connection,
            Rule54ValidationRequest request,
            Rule54ValidationSummary summary,
            int systemUserId,
            string? userName)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
INSERT INTO dbo.ValidationRuns
(ClientID, UserID, HemisServer, AuditDatabase, StudTable, DeceasedTable, StudColumn, DeceasedColumn,
 RuleNumber, RuleName, Status, RunTimestamp, TotalRecords, PassCount, FailCount, ExceptionRate,
 ExceptionsJSON, ResultsJSON, RunByUserName, LastEditedByUserName, LastEditedAt, PreviousHash, RecordHash, IsCurrent)
VALUES
(@ClientID, @UserID, @HemisServer, @AuditDatabase, @StudTable, @DeceasedTable, @StudColumn, @DeceasedColumn,
 @RuleNumber, @RuleName, @Status, GETDATE(), @TotalRecords, @PassCount, @FailCount, @ExceptionRate,
 @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            command.Parameters.AddWithValue("@ClientID",      request.ClientId);
            command.Parameters.AddWithValue("@UserID",        systemUserId);
            command.Parameters.AddWithValue("@HemisServer",   request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable",     request.CredTable);
            command.Parameters.AddWithValue("@DeceasedTable", request.QualTable);
            command.Parameters.AddWithValue("@StudColumn",    request.PqmTable);
            command.Parameters.AddWithValue("@DeceasedColumn", "");
            command.Parameters.AddWithValue("@RuleNumber",    54);
            command.Parameters.AddWithValue("@RuleName",      "CRED vs QUAL vs PQM Validation");
            command.Parameters.AddWithValue("@Status",        summary.FailCount == 0 ? "Pass" : "Fail");
            command.Parameters.AddWithValue("@TotalRecords",  summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount",     summary.PassCount);
            command.Parameters.AddWithValue("@FailCount",     summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@ExceptionsJSON",
                ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary.Exceptions)));
            command.Parameters.AddWithValue("@ResultsJSON",
                ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? DBNull.Value);

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 54);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var value = await command.ExecuteScalarAsync();
            var runId = Convert.ToInt32(value);

            await using var hashCmd = connection.CreateConfiguredCommand();
            hashCmd.CommandText = @"UPDATE dbo.ValidationRuns SET RecordHash = @RecordHash WHERE RunID = @RunID;";
            hashCmd.Parameters.AddWithValue("@RunID", runId);
            hashCmd.Parameters.AddWithValue("@RecordHash", ComputeHash(
                $"ValidationRun|{runId}|{request.ClientId}|{systemUserId}|{summary.Status}|" +
                $"{summary.TotalValidated}|{summary.PassCount}|{summary.FailCount}|{summary.ExceptionRate}|" +
                $"{summary.Timestamp}|{previousHash}"));
            await hashCmd.ExecuteNonQueryAsync();
            return runId;
        }

        private async Task<int> ClearSignoffsAndFlagForReviewAsync(SqlConnection connection, int runId)
        {
            await using var cnt = connection.CreateConfiguredCommand();
            cnt.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            cnt.Parameters.AddWithValue("@RunID", runId);
            var existingCount = Convert.ToInt32(await cnt.ExecuteScalarAsync());

            await using var del = connection.CreateConfiguredCommand();
            del.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            del.Parameters.AddWithValue("@RunID", runId);
            await del.ExecuteNonQueryAsync();

            await using var upd = connection.CreateConfiguredCommand();
            upd.CommandText = "UPDATE dbo.ValidationRuns SET Status = 'Needs Review' WHERE RunID = @RunID;";
            upd.Parameters.AddWithValue("@RunID", runId);
            await upd.ExecuteNonQueryAsync();

            return existingCount;
        }

        private async Task UpdateRunStatusFromSignoffsAsync(SqlConnection connection, int runId)
        {
            var hasAll = await HasAllRequiredSignoffsAsync(connection, runId);
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "UPDATE dbo.ValidationRuns SET Status = @Status WHERE RunID = @RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@Status", hasAll ? "Reviewed and Completed" : "Needs Review");
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<bool> HasAllRequiredSignoffsAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'DataAnalyst') THEN 1 ELSE 0 END AS HasDA,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Manager')     THEN 1 ELSE 0 END AS HasMgr,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Director')    THEN 1 ELSE 0 END AS HasDir;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return false;
            return (!r.IsDBNull(0) && r.GetInt32(0) == 1) &&
                   (!r.IsDBNull(1) && r.GetInt32(1) == 1) &&
                   (!r.IsDBNull(2) && r.GetInt32(2) == 1);
        }

        private async Task<int> ClearRuleSignoffsAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var cnt = connection.CreateConfiguredCommand();
            cnt.CommandText = @"SELECT COUNT(1) FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.ValidationRuns vr ON vr.RunID = rs.RunID
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = @RuleNumber;";
            cnt.Parameters.AddWithValue("@ClientID",   clientId);
            cnt.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var count = Convert.ToInt32(await cnt.ExecuteScalarAsync());

            await using var del = connection.CreateConfiguredCommand();
            del.CommandText = @"DELETE rs FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.ValidationRuns vr ON vr.RunID = rs.RunID
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = @RuleNumber;";
            del.Parameters.AddWithValue("@ClientID",   clientId);
            del.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await del.ExecuteNonQueryAsync();
            return count;
        }

        private async Task MarkPreviousRunsHistoricalAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"UPDATE dbo.ValidationRuns SET IsCurrent = 0
WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber AND IsCurrent = 1;";
            cmd.Parameters.AddWithValue("@ClientID",   clientId);
            cmd.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<List<RunSignoffViewModel>> GetRunSignoffsAsync(
            SqlConnection connection, int runId, int? currentUserId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT rs.SignoffID,
       ISNULL(rs.SignoffRole, '') AS SignoffRole,
       LTRIM(RTRIM(ISNULL(u.FirstName, '') + ' ' + ISNULL(u.LastName, ''))) AS ReviewerName,
       ISNULL(u.Email, '') AS ReviewerEmail,
       ISNULL(rs.Comment, '') AS Comment,
       rs.SignedOffAt,
       CASE WHEN @CurrentUserID IS NOT NULL AND rs.ReviewerID = @CurrentUserID THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsCurrentUser
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.Users u ON u.UserID = rs.ReviewerID
WHERE rs.RunID = @RunID
ORDER BY CASE ISNULL(rs.SignoffRole,'')
           WHEN 'DataAnalyst' THEN 1 WHEN 'Manager' THEN 2 WHEN 'Director' THEN 3 ELSE 4 END,
         rs.SignedOffAt DESC;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@CurrentUserID", currentUserId.HasValue ? currentUserId.Value : DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            var list = new List<RunSignoffViewModel>();
            while (await r.ReadAsync())
            {
                list.Add(new RunSignoffViewModel
                {
                    Id            = r.GetInt32(0),
                    SignoffRole   = r.IsDBNull(1) ? "" : r.GetString(1),
                    ReviewerName  = r.IsDBNull(2) ? "" : r.GetString(2),
                    ReviewerEmail = r.IsDBNull(3) ? "" : r.GetString(3),
                    Comment       = r.IsDBNull(4) ? "" : r.GetString(4),
                    SignedOffAt   = r.IsDBNull(5) ? DateTime.UtcNow : r.GetDateTime(5),
                    IsCurrentUser = !r.IsDBNull(6) && r.GetBoolean(6)
                });
            }
            return list;
        }

        private async Task<int?> GetSystemUserIdByEmailAsync(SqlConnection connection, string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            cmd.Parameters.AddWithValue("@Email", email);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToInt32(v);
        }

        private async Task<string?> GetEngagementRoleAsync(SqlConnection connection, int clientId, int userId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"SELECT TOP 1 EngagementRole FROM dbo.UserClientAssignments
WHERE ClientID = @ClientID AND UserID = @UserID;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            cmd.Parameters.AddWithValue("@UserID",   userId);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToString(v);
        }

        private static async Task<bool> IsWorkspaceSavedAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"SELECT CASE WHEN EXISTS (
    SELECT 1 FROM dbo.ValidationRuns
    WHERE RunID = @RunID
      AND (WorkspaceSavedAt IS NOT NULL
           OR EXISTS (SELECT 1 FROM dbo.ReviewSignoffs rs
                      WHERE rs.RunID = ValidationRuns.RunID AND rs.SignoffRole = 'DataAnalyst'))
) THEN 1 ELSE 0 END;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        private async Task<bool> HasSignoffRoleAsync(SqlConnection connection, int runId, string signoffRole)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole=@SignoffRole;";
            cmd.Parameters.AddWithValue("@RunID",       runId);
            cmd.Parameters.AddWithValue("@SignoffRole", signoffRole);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        private async Task<string?> GetValidationRecordHashAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToString(v);
        }

        private async Task<string?> GetLatestValidationHashAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"SELECT TOP 1 RecordHash FROM dbo.ValidationRuns
WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber AND RecordHash IS NOT NULL
ORDER BY RunTimestamp DESC, RunID DESC;";
            cmd.Parameters.AddWithValue("@ClientID",   clientId);
            cmd.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToString(v);
        }

        private async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 Status FROM dbo.Clients WHERE ClientID = @ClientID;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            var status = Convert.ToString(await cmd.ExecuteScalarAsync());
            if (string.Equals(status, "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Archived engagements are read-only.");
        }

        private async Task<SqlConnection> OpenSystemConnectionAsync()
        {
            var server   = _configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = _configuration["SystemDatabase:Name"] ?? "HEMISBaseSystem";
            var trust    = _configuration.GetValue("SystemDatabase:TrustServerCertificate", true);

            var builder = new SqlConnectionStringBuilder
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

        private static Rule54ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule54ValidationSummary>(decoded);
            }
            catch { return null; }
        }

        private static void ApplyBrowserPreview(Rule54ValidationSummary summary)
        {
            summary.ValidationRows = summary.ValidationRows.Take(BrowserPreviewRowLimit).ToList();
            summary.Exceptions     = summary.Exceptions.Take(BrowserPreviewRowLimit).ToList();
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager",     StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director",    StringComparison.OrdinalIgnoreCase);

        private static string Sanitise(string? name) =>
            (name ?? "").Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }
    }
}
