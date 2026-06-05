using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule57Service : IRule57Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule57Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
        {
            _configuration = configuration;
            _pendingValidationCache = pendingValidationCache;
        }

        private static string BuildConnectionString(string server, string database, string driver) =>
            $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";

        // ── Public API ───────────────────────────────────────────────────────

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

        public async Task<Rule57TableListResult> GetTablesAsync(string server, string database, string driver)
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

                return new Rule57TableListResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = tables.FirstOrDefault(t =>
                        t.Equals("dbo_STUD", StringComparison.OrdinalIgnoreCase)),
                    AutoCregTable = tables.FirstOrDefault(t =>
                        t.Equals("dbo_CREG", StringComparison.OrdinalIgnoreCase))
                };
            }
            catch (Exception ex)
            {
                return new Rule57TableListResult { Success = false, Error = ex.Message };
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
                    "stud_id"       => columns.FirstOrDefault(c => c.Equals("_007", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "stud_code"     => columns.FirstOrDefault(c => c.Equals("_001", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "stud_regtype"  => columns.FirstOrDefault(c => c.Equals("_024", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "creg_id"       => columns.FirstOrDefault(c => c.Equals("_007", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "creg_code"     => columns.FirstOrDefault(c => c.Equals("_001", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "creg_regtype"  => columns.FirstOrDefault(c => c.Equals("_064", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
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

        public async Task<Rule57VerifyResult> VerifyDataAsync(Rule57VerifyRequest request)
        {
            try
            {
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var st   = Sanitise(request.StudTable);
                var ct   = Sanitise(request.CregTable);
                var fCol = Sanitise(request.CregRegTypeCol);
                var fVal = NormalizeFilterValue(request.CregRegTypeFilterValue);

                var sql = $@"
SELECT
    (SELECT COUNT(*) FROM [{st}]) AS Stud_Total,
    (SELECT COUNT(*) FROM [{ct}]) AS Creg_Total,
    (SELECT COUNT(*) FROM [{ct}]
     WHERE UPPER(LTRIM(RTRIM(CAST([{fCol}] AS NVARCHAR(255))))) = '{fVal}') AS Filtered_Total";

                using var cmd = new SqlCommand(sql, conn).WithLargeDataTimeout();
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new Rule57VerifyResult
                    {
                        Success       = true,
                        StudTotal     = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
                        CregTotal     = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                        FilteredTotal = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2)),
                        FilterColumn  = request.CregRegTypeCol,
                        FilterValue   = fVal
                    };
                }
                return new Rule57VerifyResult { Success = false, Error = "No data returned" };
            }
            catch (Exception ex)
            {
                return new Rule57VerifyResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule57ValidationSummary> RunValidationAsync(
            Rule57ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var st   = Sanitise(request.StudTable);
                var ct   = Sanitise(request.CregTable);
                var si   = Sanitise(request.StudIdCol);
                var sc   = Sanitise(request.StudCodeCol);
                var srt  = Sanitise(request.StudRegTypeCol);
                var ci   = Sanitise(request.CregIdCol);
                var cc   = Sanitise(request.CregCodeCol);
                var crt  = Sanitise(request.CregRegTypeCol);
                var fVal = NormalizeFilterValue(request.CregRegTypeFilterValue);

                // Filter on CREG._064 = filter value, join with STUD on student ID,
                // validate that STUD._024 = CREG._064 and STUD._001 = CREG._001
                var sql = $@"
SELECT
    c.[{ci}] AS StudentId,
    c.[{cc}] AS CregCode,
    c.[{crt}] AS CregRegType,
    s.[{sc}]  AS StudCode,
    s.[{srt}] AS StudRegType
FROM [{ct}] c
LEFT JOIN [{st}] s
  ON UPPER(LTRIM(RTRIM(CAST(s.[{si}] AS NVARCHAR(255)))))
   = UPPER(LTRIM(RTRIM(CAST(c.[{ci}] AS NVARCHAR(255)))))
WHERE UPPER(LTRIM(RTRIM(CAST(c.[{crt}] AS NVARCHAR(255))))) = '{fVal}'";

                var validationRows = new List<Rule57ValidationRow>();
                int rowNo = 0;

                using (var cmd = new SqlCommand(sql, conn).WithLargeDataTimeout())
                using (var r = await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        rowNo++;
                        var studentId   = r.IsDBNull(0) ? "" : r.GetValue(0)?.ToString() ?? "";
                        var cregCode    = r.IsDBNull(1) ? "" : r.GetValue(1)?.ToString() ?? "";
                        var cregRegType = r.IsDBNull(2) ? "" : r.GetValue(2)?.ToString() ?? "";
                        var studCode    = r.IsDBNull(3) ? null : r.GetValue(3)?.ToString();
                        var studRegType = r.IsDBNull(4) ? null : r.GetValue(4)?.ToString();

                        string result;
                        string? reason;

                        if (studCode == null)
                        {
                            result = "FAIL";
                            reason = "Student not found in dbo_STUD";
                        }
                        else if (!string.Equals(
                            studRegType?.Trim().ToUpperInvariant() ?? "",
                            cregRegType.Trim().ToUpperInvariant(),
                            StringComparison.Ordinal))
                        {
                            result = "FAIL";
                            reason = $"Registration type mismatch — STUD._024: '{studRegType?.Trim()}', CREG._064: '{cregRegType.Trim()}'";
                        }
                        else
                        {
                            result = "PASS";
                            reason = null;
                        }

                        validationRows.Add(new Rule57ValidationRow
                        {
                            ValidationNumber = rowNo,
                            StudentId        = studentId,
                            StudCode         = studCode ?? "",
                            StudRegType      = studRegType ?? "",
                            CregCode         = cregCode,
                            CregRegType      = cregRegType,
                            ValidationResult = result,
                            ExceptionReason  = reason
                        });
                    }
                }

                var total     = validationRows.Count;
                var passCount = validationRows.Count(row => row.ValidationResult == "PASS");
                var failCount = validationRows.Count(row => row.ValidationResult == "FAIL");
                var rate      = total > 0 ? Math.Round((decimal)failCount / total * 100, 2) : 0;

                var exceptions = validationRows
                    .Where(row => row.ValidationResult == "FAIL")
                    .Select(row => new Rule57ExceptionRecord
                    {
                        ValidationNumber = row.ValidationNumber,
                        StudentId        = row.StudentId,
                        StudCode         = row.StudCode,
                        StudRegType      = row.StudRegType,
                        CregCode         = row.CregCode,
                        CregRegType      = row.CregRegType,
                        ValidationResult = row.ValidationResult,
                        ExceptionReason  = row.ExceptionReason ?? ""
                    })
                    .ToList();

                var summary = new Rule57ValidationSummary
                {
                    Success                 = true,
                    TotalValidated          = total,
                    PassCount               = passCount,
                    FailCount               = failCount,
                    ExceptionRate           = rate,
                    Status                  = failCount == 0 ? "PASS" : "FAIL",
                    Timestamp               = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Database                = request.Database,
                    StudTable               = request.StudTable,
                    CregTable               = request.CregTable,
                    StudIdCol               = request.StudIdCol,
                    StudCodeCol             = request.StudCodeCol,
                    StudRegTypeCol          = request.StudRegTypeCol,
                    CregIdCol               = request.CregIdCol,
                    CregCodeCol             = request.CregCodeCol,
                    CregRegTypeCol          = request.CregRegTypeCol,
                    CregRegTypeFilterValue  = fVal,
                    ClientId                = request.ClientId,
                    ValidationRows          = validationRows,
                    Exceptions              = exceptions
                };

                if (request.ClientId > 0)
                {
                    await using var systemConnection = await OpenSystemConnectionAsync();
                    var systemUserId = await GetSystemUserIdByEmailAsync(systemConnection, userEmail);
                    if (!systemUserId.HasValue)
                        throw new InvalidOperationException("Current user is not available in the system database.");

                    await EnsureClientNotArchivedAsync(systemConnection, request.ClientId);
                    await ClearRuleSignoffsAsync(systemConnection, request.ClientId, 57);
                    await MarkPreviousRunsHistoricalAsync(systemConnection, request.ClientId, 57);

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
                return new Rule57ValidationSummary { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule57WorkspaceSaveResult> SaveWorkspaceAsync(
            Rule57ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                    return new Rule57WorkspaceSaveResult { Success = false, Error = "Run validation before saving the workspace." };

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                    return new Rule57WorkspaceSaveResult { Success = false, Error = "The saved workspace could not be found for this engagement." };

                await EnsureClientNotArchivedAsync(connection, request.ClientId);

                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                    return new Rule57WorkspaceSaveResult { Success = false, Error = "Current user is not available in the system database." };

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
                command.Parameters.AddWithValue("@StudTable",           request.StudTable);
                command.Parameters.AddWithValue("@DeceasedTable",       request.CregTable);
                command.Parameters.AddWithValue("@StudColumn",          request.StudRegTypeCol);
                command.Parameters.AddWithValue("@DeceasedColumn",      NormalizeFilterValue(request.CregRegTypeFilterValue));
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash",        (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash(
                    $"WorkspaceSave|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|" +
                    $"{request.StudTable}|{request.CregTable}|" +
                    $"{request.CregRegTypeFilterValue}|" +
                    $"{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail);
                return new Rule57WorkspaceSaveResult
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
                return new Rule57WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule57WorkspaceSaveResult> BeginWorkspaceEditAsync(
            int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                    return new Rule57WorkspaceSaveResult { Success = false, Error = "Current user is not available in the system database." };

                var clientId = await GetClientIdForRunAsync(runId);
                if (!clientId.HasValue)
                    return new Rule57WorkspaceSaveResult { Success = false, Error = "Saved workspace was not found." };

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
                return new Rule57WorkspaceSaveResult
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
                return new Rule57WorkspaceSaveResult { Success = false, Error = ex.Message };
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

        public async Task<Rule57WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(
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
    ISNULL(vr.StudTable,     '') AS StudTable,
    ISNULL(vr.DeceasedTable, '') AS CregTable,
    ISNULL(vr.StudColumn,    '') AS StudRegTypeCol,
    ISNULL(vr.DeceasedColumn,'') AS CregRegTypeFilterValue,
    ISNULL(vr.Status,        '') AS Status,
    ISNULL(vr.LastEditedByUserName, '') AS LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID   = @ClientID
  AND vr.RuleNumber = 57
ORDER BY vr.IsCurrent DESC, vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summaryJson = reader.IsDBNull(11) ? null : reader.GetString(11);
            var summary = DeserializeSummary(summaryJson);
            if (summary != null)
                ApplyBrowserPreview(summary);

            var dbFilterVal = reader.IsDBNull(7) ? "" : reader.GetString(7);
            var restoredFilterValue = summary?.CregRegTypeFilterValue is { Length: > 0 } v ? v : dbFilterVal;

            var workspace = new Rule57WorkspaceStateViewModel
            {
                RunId                  = reader.GetInt32(0),
                ClientId               = reader.GetInt32(1),
                Server                 = reader.GetString(2),
                Database               = reader.GetString(3),
                Driver                 = "ODBC Driver 17 for SQL Server",
                StudTable              = reader.GetString(4),
                CregTable              = reader.GetString(5),
                CregRegTypeFilterValue = restoredFilterValue,
                CurrentStatus          = reader.GetString(8),
                LastEditedByUserName   = reader.IsDBNull(9)  ? null : reader.GetString(9),
                LastEditedAt           = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Summary                = summary
            };

            if (summary != null)
            {
                workspace.StudIdCol              = summary.StudIdCol;
                workspace.StudCodeCol            = summary.StudCodeCol;
                workspace.StudRegTypeCol         = summary.StudRegTypeCol;
                workspace.CregIdCol              = summary.CregIdCol;
                workspace.CregCodeCol            = summary.CregCodeCol;
                workspace.CregRegTypeCol         = summary.CregRegTypeCol;
                workspace.CregRegTypeFilterValue = restoredFilterValue;
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

        public async Task<Rule57RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
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

            var viewModel = new Rule57RunReviewViewModel
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

        public string GenerateSql(Rule57ValidationRequest request)
        {
            var st   = request.StudTable;
            var ct   = request.CregTable;
            var si   = request.StudIdCol;
            var sc   = request.StudCodeCol;
            var srt  = request.StudRegTypeCol;
            var ci   = request.CregIdCol;
            var cc   = request.CregCodeCol;
            var crt  = request.CregRegTypeCol;
            var fVal = NormalizeFilterValue(request.CregRegTypeFilterValue);

            return $@"-- ============================================================================
-- HEMIS RULE 57  -  REGISTRATION DOCUMENTATION AGREEMENT
-- Generated  : {DateTime.Now:yyyy-MM-dd HH:mm:ss}
-- Description: Agrees to the student's signed application and/or registration documentation.
-- Filter     : {ct}.[{crt}] = '{fVal}'
-- ============================================================================
-- TABLES
--   {ct}  : [{ci}] (student ID), [{cc}] (code), [{crt}] (registration type, filter = '{fVal}')
--   {st}  : [{si}] (student ID), [{sc}] (code), [{srt}] (registration type)
-- JOIN:      {ct}.[{ci}] = {st}.[{si}]
-- PASS:      {st}.[{srt}] = {ct}.[{crt}]  (registration types agree)
-- FAIL:      Student not found in {st} | registration type mismatch
-- ============================================================================

-- Step 1: Filtered CREG records joined to dbo_STUD
SELECT
    c.[{ci}]  AS Student_ID,
    c.[{cc}]  AS CREG_Code,
    c.[{crt}] AS CREG_Reg_Type,
    s.[{sc}]  AS STUD_Code,
    s.[{srt}] AS STUD_Reg_Type,
    CASE
        WHEN s.[{si}] IS NULL
            THEN 'FAIL  -  Student not found in {st}'
        WHEN UPPER(LTRIM(RTRIM(CAST(s.[{srt}] AS NVARCHAR(255)))))
          <> UPPER(LTRIM(RTRIM(CAST(c.[{crt}] AS NVARCHAR(255)))))
            THEN 'FAIL  -  Registration type mismatch (STUD._024 <> CREG._064)'
        ELSE 'PASS'
    END AS Validation_Result
FROM [{ct}] c
LEFT JOIN [{st}] s
  ON UPPER(LTRIM(RTRIM(CAST(s.[{si}] AS NVARCHAR(255)))))
   = UPPER(LTRIM(RTRIM(CAST(c.[{ci}] AS NVARCHAR(255)))))
WHERE UPPER(LTRIM(RTRIM(CAST(c.[{crt}] AS NVARCHAR(255))))) = '{fVal}'
ORDER BY c.[{ci}];

-- Step 2: Summary
SELECT
    COUNT(*) AS Total_Filtered,
    SUM(CASE WHEN s.[{si}] IS NOT NULL
         AND UPPER(LTRIM(RTRIM(CAST(s.[{srt}] AS NVARCHAR(255)))))
           = UPPER(LTRIM(RTRIM(CAST(c.[{crt}] AS NVARCHAR(255)))))
         THEN 1 ELSE 0 END) AS Pass_Count,
    SUM(CASE WHEN s.[{si}] IS NULL
         OR UPPER(LTRIM(RTRIM(CAST(s.[{srt}] AS NVARCHAR(255)))))
         <> UPPER(LTRIM(RTRIM(CAST(c.[{crt}] AS NVARCHAR(255)))))
         THEN 1 ELSE 0 END) AS Fail_Count
FROM [{ct}] c
LEFT JOIN [{st}] s
  ON UPPER(LTRIM(RTRIM(CAST(s.[{si}] AS NVARCHAR(255)))))
   = UPPER(LTRIM(RTRIM(CAST(c.[{ci}] AS NVARCHAR(255)))))
WHERE UPPER(LTRIM(RTRIM(CAST(c.[{crt}] AS NVARCHAR(255))))) = '{fVal}';

-- Step 3: Exceptions only
SELECT
    c.[{ci}]  AS Student_ID,
    c.[{cc}]  AS CREG_Code,
    c.[{crt}] AS CREG_Reg_Type,
    s.[{sc}]  AS STUD_Code,
    s.[{srt}] AS STUD_Reg_Type
FROM [{ct}] c
LEFT JOIN [{st}] s
  ON UPPER(LTRIM(RTRIM(CAST(s.[{si}] AS NVARCHAR(255)))))
   = UPPER(LTRIM(RTRIM(CAST(c.[{ci}] AS NVARCHAR(255)))))
WHERE UPPER(LTRIM(RTRIM(CAST(c.[{crt}] AS NVARCHAR(255))))) = '{fVal}'
  AND (
        s.[{si}] IS NULL
        OR UPPER(LTRIM(RTRIM(CAST(s.[{srt}] AS NVARCHAR(255)))))
        <> UPPER(LTRIM(RTRIM(CAST(c.[{crt}] AS NVARCHAR(255)))))
      )
ORDER BY c.[{ci}];
-- ============================================================================
-- END OF RULE 57 REGISTRATION DOCUMENTATION AGREEMENT
-- ============================================================================
";
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private async Task<int> InsertValidationRunAsync(
            SqlConnection connection,
            Rule57ValidationRequest request,
            Rule57ValidationSummary summary,
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
            command.Parameters.AddWithValue("@StudTable",     request.StudTable);
            command.Parameters.AddWithValue("@DeceasedTable", request.CregTable);
            command.Parameters.AddWithValue("@StudColumn",    request.StudRegTypeCol);
            command.Parameters.AddWithValue("@DeceasedColumn", NormalizeFilterValue(request.CregRegTypeFilterValue));
            command.Parameters.AddWithValue("@RuleNumber",    57);
            command.Parameters.AddWithValue("@RuleName",      "Registration Documentation Agreement");
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

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 57);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var value = await command.ExecuteScalarAsync();
            var runId = Convert.ToInt32(value);

            await using var hashCmd = connection.CreateConfiguredCommand();
            hashCmd.CommandText = @"UPDATE dbo.ValidationRuns SET RecordHash = @RecordHash WHERE RunID = @RunID;";
            hashCmd.Parameters.AddWithValue("@RunID", runId);
            hashCmd.Parameters.AddWithValue("@RecordHash", ComputeHash(
                $"ValidationRun|{runId}|{request.ClientId}|{systemUserId}|{summary.Status}|" +
                $"{summary.TotalValidated}|{summary.PassCount}|{summary.FailCount}|{summary.ExceptionRate}|" +
                $"{summary.Timestamp}|{request.CregRegTypeFilterValue}|{previousHash}"));
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

        private static Rule57ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule57ValidationSummary>(decoded);
            }
            catch { return null; }
        }

        private static void ApplyBrowserPreview(Rule57ValidationSummary summary)
        {
            summary.ValidationRows = summary.ValidationRows.Take(BrowserPreviewRowLimit).ToList();
            summary.Exceptions     = summary.Exceptions.Take(BrowserPreviewRowLimit).ToList();
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager",     StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director",    StringComparison.OrdinalIgnoreCase);

        private static string NormalizeFilterValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToUpperInvariant();

        private static string Sanitise(string? name) =>
            Regex.Replace((name ?? "").Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim(),
                @"[^\w\-\. ]", "");

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }
    }
}
