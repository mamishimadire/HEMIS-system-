using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Security.Cryptography;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule36Service : IRule36Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule36Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
        {
            _configuration = configuration;
            _pendingValidationCache = pendingValidationCache;
        }

        private static string BuildConnectionString(string server, string database, string driver) =>
            $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";

        public async Task<DatabaseListResult> GetDatabasesAsync(string server, string driver)
        {
            try
            {
                var connStr = $"Server={server};Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT name FROM sys.databases WHERE name NOT IN ('master','tempdb','model','msdb') ORDER BY name", conn)
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

        public async Task<TableListResult> GetTablesAsync(string server, string database, string driver)
        {
            try
            {
                var connStr = BuildConnectionString(server, database, driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME", conn)
                    .WithLargeDataTimeout();
                using var reader = await cmd.ExecuteReaderAsync();
                var tables = new List<string>();
                while (await reader.ReadAsync())
                    tables.Add(reader.GetString(0));
                var autoStud = tables.FirstOrDefault(t => t.Equals("dbo_STUD", StringComparison.OrdinalIgnoreCase));
                var autoDec = tables.FirstOrDefault(t => t.Contains("deceased", StringComparison.OrdinalIgnoreCase));
                return new TableListResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = autoStud,
                    AutoDeceasedTable = autoDec
                };
            }
            catch (Exception ex)
            {
                return new TableListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName, bool isStudTable)
        {
            try
            {
                var connStr = BuildConnectionString(server, database, driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME=@t ORDER BY ORDINAL_POSITION", conn)
                    .WithLargeDataTimeout();
                cmd.Parameters.AddWithValue("@t", tableName);
                using var reader = await cmd.ExecuteReaderAsync();
                var columns = new List<string>();
                while (await reader.ReadAsync())
                    columns.Add(reader.GetString(0));

                string? autoSelected = null;
                string Normalize(string value) => value.Trim().ToLowerInvariant();
                var normalizedColumns = columns
                    .Select(c => new { Original = c, Normalized = Normalize(c) })
                    .ToList();

                if (isStudTable)
                {
                    autoSelected =
                        columns.FirstOrDefault(c => c.Equals("_007", StringComparison.OrdinalIgnoreCase)) ??
                        normalizedColumns.FirstOrDefault(c =>
                            c.Normalized.Contains("007") ||
                            c.Normalized.Contains("student") ||
                            c.Normalized.Contains("stud") ||
                            c.Normalized.EndsWith("id") ||
                            c.Normalized.Contains("number") ||
                            c.Normalized.Contains("code"))?.Original;
                }
                else
                {
                    var priorities = new[]
                    {
                        "STUDENT_INUMBER",
                        "STUDENT_NUMBER",
                        "STUDENT_ID",
                        "STUDENT_NO",
                        "STUDENTNUMBER",
                        "STUDENT_ID_NO",
                        "_007"
                    };

                    autoSelected = priorities
                        .Select(p => columns.FirstOrDefault(c => c.Equals(p, StringComparison.OrdinalIgnoreCase)))
                        .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

                    autoSelected ??= normalizedColumns.FirstOrDefault(c =>
                        c.Normalized.Contains("student") ||
                        c.Normalized.Contains("stud") ||
                        c.Normalized.Contains("id") ||
                        c.Normalized.Contains("number") ||
                        c.Normalized.Contains("code") ||
                        c.Normalized.Contains("inumber"))?.Original;
                }

                if (string.IsNullOrWhiteSpace(autoSelected) && columns.Count > 0)
                {
                    autoSelected = columns[0];
                }

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

        public async Task<VerifyResult> VerifyDataAsync(VerifyRequest request)
        {
            try
            {
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                var st = Sanitise(request.StudTable);
                var dt = Sanitise(request.DeceasedTable);
                var sc = Sanitise(request.StudColumn);
                var dc = Sanitise(request.DeceasedColumn);
                var sql = $@"
WITH DeceasedKeys AS
(
    SELECT DISTINCT CONVERT(nvarchar(255), d.[{dc}]) AS DeceasedKey
    FROM [{dt}] d
    WHERE d.[{dc}] IS NOT NULL
),
StudentKeys AS
(
    SELECT CONVERT(nvarchar(255), s.[{sc}]) AS StudentKey
    FROM [{st}] s
)
SELECT
                    (SELECT COUNT(*) FROM [{st}]) AS STUD_Total,
                    (SELECT COUNT(*) FROM [{dt}]) AS Deceased_Total,
                    (SELECT COUNT(*)
                     FROM StudentKeys s
                     INNER JOIN DeceasedKeys d
                        ON d.DeceasedKey = s.StudentKey) AS Matching_Records";
                using var cmd = new SqlCommand(sql, conn).WithLargeDataTimeout();
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new VerifyResult
                    {
                        Success = true,
                        StudTotal = Convert.ToInt32(reader.GetInt64(0)),
                        DeceasedTotal = Convert.ToInt32(reader.GetInt64(1)),
                        MatchingRecords = Convert.ToInt32(reader.GetInt64(2))
                    };
                }

                return new VerifyResult { Success = false, Error = "No data returned" };
            }
            catch (Exception ex)
            {
                return new VerifyResult { Success = false, Error = ex.Message };
            }
        }

        public Task<ValidationSummary> GetValidationSummaryAsync(ValidationRequest request) =>
            RunValidationAsync(request);

        public async Task<ValidationSummary> RunValidationAsync(ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var st = Sanitise(request.StudTable);
                var dt = Sanitise(request.DeceasedTable);
                var sc = Sanitise(request.StudColumn);
                var dc = Sanitise(request.DeceasedColumn);

                var sql = $@"
WITH DeceasedKeys AS
(
    SELECT DISTINCT CONVERT(nvarchar(255), d.[{dc}]) AS DeceasedKey
    FROM [{dt}] d
    WHERE d.[{dc}] IS NOT NULL
)
SELECT
    ROW_NUMBER() OVER (ORDER BY s.[{sc}]) AS Validation_Number,
    CASE WHEN d.DeceasedKey IS NOT NULL THEN 'FAIL' ELSE 'PASS' END AS Validation_Result,
    CASE WHEN d.DeceasedKey IS NOT NULL THEN 'Student marked as deceased' ELSE NULL END AS Exception_Reason,
    CONVERT(nvarchar(255), s.[{sc}]) AS STUD_Column_Value
FROM [{st}] s
LEFT JOIN DeceasedKeys d
    ON d.DeceasedKey = CONVERT(nvarchar(255), s.[{sc}])
ORDER BY s.[{sc}]";

                using var cmd = new SqlCommand(sql, conn).WithLargeDataTimeout();
                using var reader = await cmd.ExecuteReaderAsync();

                var records = new List<(int Row, string Result, string? Reason, string StudentId)>();
                while (await reader.ReadAsync())
                {
                    records.Add((
                        Convert.ToInt32(reader.GetInt64(0)),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? "" : reader.GetValue(3)?.ToString() ?? ""));
                }

                var total = records.Count;
                var passCount = records.Count(r => r.Result == "PASS");
                var failCount = records.Count(r => r.Result == "FAIL");
                var rate = total > 0 ? Math.Round((decimal)failCount / total * 100, 2) : 0;

                var validationRows = records.Select(r => new ValidationRowRecord
                {
                    ValidationNumber = r.Row,
                    ValidationResult = r.Result,
                    ExceptionReason = r.Reason,
                    StudentId = r.StudentId
                }).ToList();

                var exceptions = records
                    .Where(r => r.Result == "FAIL")
                    .Select(r => new ExceptionRecord
                    {
                        ValidationNumber = r.Row,
                        StudentId = r.StudentId,
                        ExceptionReason = r.Reason ?? "Student marked as deceased",
                        ValidationResult = r.Result
                    })
                    .ToList();

                var summary = new ValidationSummary
                {
                    Success = true,
                    TotalValidated = total,
                    PassCount = passCount,
                    FailCount = failCount,
                    ExceptionRate = rate,
                    Status = failCount == 0 ? "PASS" : "FAIL",
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    StudTable = request.StudTable,
                    DeceasedTable = request.DeceasedTable,
                    StudColumn = request.StudColumn,
                    DeceasedColumn = request.DeceasedColumn,
                    Database = request.Database,
                    ClientId = request.ClientId,
                    ValidationRows = validationRows,
                    Exceptions = exceptions
                };

                if (request.ClientId > 0)
                {
                    await using var systemConnection = await OpenSystemConnectionAsync();
                    var systemUserId = await GetSystemUserIdByEmailAsync(systemConnection, userEmail);
                    if (!systemUserId.HasValue)
                        throw new InvalidOperationException("Current user is not available in the system database.");

                    await EnsureClientNotArchivedAsync(systemConnection, request.ClientId);

                    await ClearRuleSignoffsAsync(systemConnection, request.ClientId, 36);
                    await MarkPreviousRunsHistoricalAsync(systemConnection, request.ClientId, 36);

                    var runId = await InsertValidationRunAsync(systemConnection, request, summary, systemUserId.Value, userName);
                    summary.SavedRunId = runId;

                    await using var update = systemConnection.CreateConfiguredCommand();
                    update.CommandText = @"
UPDATE dbo.ValidationRuns
SET ResultsJSON = @ResultsJSON
WHERE RunID = @RunID;";
                    update.Parameters.AddWithValue("@RunID", runId);
                    update.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
                    await update.ExecuteNonQueryAsync();
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new ValidationSummary { Success = false, Error = ex.Message };
            }
        }

        public async Task<WorkspaceSaveResult> SaveWorkspaceAsync(ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                {
                    return new WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run validation before saving the workspace."
                    };
                }

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                {
                    return new WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "The saved workspace could not be found for this engagement."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, request.ClientId);

                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                {
                    return new WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Current user is not available in the system database."
                    };
                }

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                var previousHash = await GetValidationRecordHashAsync(connection, request.RunId.Value);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer = @HemisServer,
    AuditDatabase = @AuditDatabase,
    StudTable = @StudTable,
    DeceasedTable = @DeceasedTable,
    StudColumn = @StudColumn,
    DeceasedColumn = @DeceasedColumn,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    WorkspaceSavedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID
  AND ClientID = @ClientID;";
                command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                command.Parameters.AddWithValue("@ClientID", request.ClientId);
                command.Parameters.AddWithValue("@HemisServer", request.Server);
                command.Parameters.AddWithValue("@AuditDatabase", request.Database);
                command.Parameters.AddWithValue("@StudTable", request.StudTable);
                command.Parameters.AddWithValue("@DeceasedTable", request.DeceasedTable);
                command.Parameters.AddWithValue("@StudColumn", request.StudColumn);
                command.Parameters.AddWithValue("@DeceasedColumn", request.DeceasedColumn);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|{request.StudTable}|{request.DeceasedTable}|{request.StudColumn}|{request.DeceasedColumn}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail);
                return new WorkspaceSaveResult
                {
                    Success = true,
                    Message = clearedSignoffs > 0
                        ? "Workspace saved. Existing signoffs were removed and the run must be reviewed again."
                        : "Workspace saved and marked for review again.",
                    SignoffsCleared = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                {
                    return new WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Current user is not available in the system database."
                    };
                }

                var clientId = await GetClientIdForRunAsync(runId);
                if (!clientId.HasValue)
                {
                    return new WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Saved workspace was not found."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, clientId.Value);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash = await GetValidationRecordHashAsync(connection, runId);
                await using (var markEdit = connection.CreateConfiguredCommand())
                {
                    markEdit.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    WorkspaceSavedAt = NULL,
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID;";
                    markEdit.Parameters.AddWithValue("@RunID", runId);
                    markEdit.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? reviewerEmail);
                    markEdit.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                    markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceEdit|{runId}|{reviewerName ?? reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                    await markEdit.ExecuteNonQueryAsync();
                }
                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail);

                return new WorkspaceSaveResult
                {
                    Success = true,
                    Message = clearedSignoffs > 0
                        ? "Editing has begun. Existing signoffs were removed so the workspace must be reviewed again."
                        : "Editing has begun.",
                    SignoffsCleared = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
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

        public async Task<Rule36WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID,
    vr.ClientID,
    ISNULL(vr.HemisServer, '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable, '') AS StudTable,
    ISNULL(vr.DeceasedTable, '') AS DeceasedTable,
    ISNULL(vr.StudColumn, '') AS StudColumn,
    ISNULL(vr.DeceasedColumn, '') AS DeceasedColumn,
    ISNULL(vr.Status, '') AS Status,
    ISNULL(vr.LastEditedByUserName, '') AS LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 36
ORDER BY vr.IsCurrent DESC, vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summaryJson = reader.IsDBNull(11) ? null : reader.GetString(11);
            var summary = DeserializeSummary(summaryJson);
            if (summary != null)
            {
                ApplyBrowserPreview(summary);
            }

            var workspace = new Rule36WorkspaceStateViewModel
            {
                RunId = reader.GetInt32(0),
                ClientId = reader.GetInt32(1),
                Server = reader.GetString(2),
                Database = reader.GetString(3),
                Driver = "ODBC Driver 17 for SQL Server",
                StudTable = reader.GetString(4),
                DeceasedTable = reader.GetString(5),
                StudColumn = reader.GetString(6),
                DeceasedColumn = reader.GetString(7),
                CurrentStatus = reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9) ? null : reader.GetString(9),
                LastEditedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Summary = summary
            };

            await reader.CloseAsync();

            int? currentUserId = null;
            if (!string.IsNullOrWhiteSpace(currentUserEmail))
            {
                currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);
                if (currentUserId.HasValue)
                {
                    workspace.CurrentUserEngagementRole =
                        await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? "";
                }
            }

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId.Value, currentUserId);
            workspace.HasDataAnalystSignoff = signoffs.Any(s =>
                string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            var currentRoleSignoff = signoffs.FirstOrDefault(s =>
                HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff = currentRoleSignoff != null;
            workspace.CurrentUserSignoffComment = currentRoleSignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved = await IsWorkspaceSavedAsync(connection, workspace.RunId!.Value);

            if (workspace.Summary != null)
                workspace.Summary.SavedRunId = workspace.RunId;

            if (string.IsNullOrWhiteSpace(workspace.CurrentStatus))
                workspace.CurrentStatus = workspace.Summary?.Status ?? "";

            return workspace;
        }

        public async Task<Rule36RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
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

            var viewModel = new Rule36RunReviewViewModel
            {
                RunId = reader.GetInt32(0),
                ClientId = reader.GetInt32(1),
                IsCurrentRun = !reader.IsDBNull(2) && reader.GetBoolean(2),
                EngagementName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                MaconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Summary = summary
            };

            await reader.CloseAsync();

            int? currentUserId = null;
            if (!string.IsNullOrWhiteSpace(currentUserEmail))
            {
                currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);
                if (currentUserId.HasValue)
                    viewModel.CurrentUserEngagementRole = await GetEngagementRoleAsync(connection, viewModel.ClientId, currentUserId.Value) ?? "";
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
IF EXISTS (
    SELECT 1
    FROM dbo.ReviewSignoffs
    WHERE RunID = @RunID
      AND ReviewerID = @ReviewerID
)
BEGIN
    UPDATE dbo.ReviewSignoffs
    SET SignoffRole = @SignoffRole,
        ReviewType = 'Final',
        Comment = @Comment,
        SignedOffAt = GETDATE()
    WHERE RunID = @RunID
      AND ReviewerID = @ReviewerID;
END
ELSE
BEGIN
    INSERT INTO dbo.ReviewSignoffs (ClientID, RunID, ReviewerID, SignoffRole, ReviewType, Comment, SignedOffAt)
    VALUES (@ClientID, @RunID, @ReviewerID, @SignoffRole, 'Final', @Comment, GETDATE());
END";
            command.Parameters.AddWithValue("@ClientID", clientId.Value);
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ReviewerID", reviewerId.Value);
            command.Parameters.AddWithValue("@SignoffRole", engagementRole!);
            command.Parameters.AddWithValue("@Comment", string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim());
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

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0)
                return;
        }

        public string GenerateSql(ValidationRequest request)
        {
            var st = request.StudTable;
            var dt = request.DeceasedTable;
            var sc = request.StudColumn;
            var dc = request.DeceasedColumn;

            return $@"-- ============================================================================
-- HEMIS 2025 - RULE 36: DECEASED STUDENTS VALIDATION
-- ============================================================================
-- Purpose: Identify deceased students by matching {sc} against {dt}
-- Tables: {st} and {dt}
-- Columns: [{st}].[{sc}] = [{dt}].[{dc}]
-- ============================================================================

IF OBJECT_ID('tempdb..#ValidationResults') IS NOT NULL DROP TABLE #ValidationResults;

WITH DeceasedKeys AS
(
    SELECT DISTINCT CONVERT(nvarchar(255), d.[{dc}]) AS DeceasedKey
    FROM [{dt}] d
    WHERE d.[{dc}] IS NOT NULL
)
SELECT
    ROW_NUMBER() OVER (ORDER BY s.[{sc}]) AS Validation_Number,
    CASE WHEN d.DeceasedKey IS NOT NULL THEN 'FAIL' ELSE 'PASS' END AS Validation_Result,
    CASE WHEN d.DeceasedKey IS NOT NULL THEN 'Student marked as deceased' ELSE NULL END AS Exception_Reason,
    CONVERT(nvarchar(255), s.[{sc}]) AS STUD_Column_Value,
    s.*
INTO #ValidationResults
FROM [{st}] s
LEFT JOIN DeceasedKeys d
    ON d.DeceasedKey = CONVERT(nvarchar(255), s.[{sc}])
ORDER BY s.[{sc}];

SELECT
    COUNT(*) AS Total_Validated,
    SUM(CASE WHEN Validation_Result = 'PASS' THEN 1 ELSE 0 END) AS Pass_Count,
    SUM(CASE WHEN Validation_Result = 'FAIL' THEN 1 ELSE 0 END) AS Fail_Count,
    CAST(SUM(CASE WHEN Validation_Result = 'FAIL' THEN 1 ELSE 0 END) * 100.0 / COUNT(*) AS DECIMAL(5,2)) AS Exception_Rate_Percent
FROM #ValidationResults;

SELECT
    Validation_Number,
    STUD_Column_Value,
    Exception_Reason,
    Validation_Result
FROM #ValidationResults
WHERE Validation_Result = 'FAIL'
ORDER BY Validation_Number;

DROP TABLE #ValidationResults;
-- ============================================================================
-- END OF RULE 36 DECEASED STUDENTS VALIDATION
-- ============================================================================
";
        }

        private async Task<int> InsertValidationRunAsync(
            SqlConnection connection,
            ValidationRequest request,
            ValidationSummary summary,
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
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.StudTable);
            command.Parameters.AddWithValue("@DeceasedTable", request.DeceasedTable);
            command.Parameters.AddWithValue("@StudColumn", request.StudColumn);
            command.Parameters.AddWithValue("@DeceasedColumn", request.DeceasedColumn);
            command.Parameters.AddWithValue("@RuleNumber", 36);
            command.Parameters.AddWithValue("@RuleName", "Deceased Students Validation");
            command.Parameters.AddWithValue("@Status", summary.FailCount == 0 ? "Pass" : "Fail");
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary.Exceptions)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? DBNull.Value);
            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 36);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
            var value = await command.ExecuteScalarAsync();
            var runId = Convert.ToInt32(value);

            await using var hashCommand = connection.CreateConfiguredCommand();
            hashCommand.CommandText = @"
UPDATE dbo.ValidationRuns
SET RecordHash = @RecordHash
WHERE RunID = @RunID;";
            hashCommand.Parameters.AddWithValue("@RunID", runId);
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|{runId}|{request.ClientId}|{systemUserId}|{summary.Status}|{summary.TotalValidated}|{summary.PassCount}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();
            return runId;
        }

        private async Task<int> ClearSignoffsAndFlagForReviewAsync(SqlConnection connection, int runId)
        {
            await using var countCommand = connection.CreateConfiguredCommand();
            countCommand.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            countCommand.Parameters.AddWithValue("@RunID", runId);
            var existingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            await using var deleteCommand = connection.CreateConfiguredCommand();
            deleteCommand.CommandText = @"
DELETE FROM dbo.ReviewSignoffs
WHERE RunID = @RunID;";
            deleteCommand.Parameters.AddWithValue("@RunID", runId);
            await deleteCommand.ExecuteNonQueryAsync();

            await using var updateCommand = connection.CreateConfiguredCommand();
            updateCommand.CommandText = @"
UPDATE dbo.ValidationRuns
SET Status = 'Needs Review'
WHERE RunID = @RunID;";
            updateCommand.Parameters.AddWithValue("@RunID", runId);
            await updateCommand.ExecuteNonQueryAsync();

            return existingCount;
        }

        private async Task UpdateRunStatusFromSignoffsAsync(SqlConnection connection, int runId)
        {
            var hasAllSignoffs = await HasAllRequiredSignoffsAsync(connection, runId);
            await SetRunStatusAsync(connection, runId, hasAllSignoffs ? "Reviewed and Completed" : "Needs Review");
        }

        private async Task<bool> HasAllRequiredSignoffsAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT
    CASE WHEN EXISTS (
        SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'DataAnalyst'
    ) THEN 1 ELSE 0 END AS HasDataAnalyst,
    CASE WHEN EXISTS (
        SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Manager'
    ) THEN 1 ELSE 0 END AS HasManager,
    CASE WHEN EXISTS (
        SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Director'
    ) THEN 1 ELSE 0 END AS HasDirector;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return false;

            var hasDataAnalyst = !reader.IsDBNull(0) && reader.GetInt32(0) == 1;
            var hasManager = !reader.IsDBNull(1) && reader.GetInt32(1) == 1;
            var hasDirector = !reader.IsDBNull(2) && reader.GetInt32(2) == 1;
            return hasDataAnalyst && hasManager && hasDirector;
        }

        private async Task SetRunStatusAsync(SqlConnection connection, int runId, string status)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.ValidationRuns
SET Status = @Status
WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@Status", status);
            await command.ExecuteNonQueryAsync();
        }

        private async Task SetRunCurrentStateAsync(SqlConnection connection, int runId, bool isCurrent)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.ValidationRuns
SET IsCurrent = @IsCurrent
WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@IsCurrent", isCurrent);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<int> ClearRuleSignoffsAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var countCommand = connection.CreateConfiguredCommand();
            countCommand.CommandText = @"
SELECT COUNT(1)
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.ValidationRuns vr ON vr.RunID = rs.RunID
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = @RuleNumber;";
            countCommand.Parameters.AddWithValue("@ClientID", clientId);
            countCommand.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var existingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            await using var deleteCommand = connection.CreateConfiguredCommand();
            deleteCommand.CommandText = @"
DELETE rs
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.ValidationRuns vr ON vr.RunID = rs.RunID
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = @RuleNumber;";
            deleteCommand.Parameters.AddWithValue("@ClientID", clientId);
            deleteCommand.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await deleteCommand.ExecuteNonQueryAsync();

            return existingCount;
        }

        private async Task MarkPreviousRunsHistoricalAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.ValidationRuns
SET IsCurrent = 0
WHERE ClientID = @ClientID
  AND RuleNumber = @RuleNumber
  AND IsCurrent = 1;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<List<RunSignoffViewModel>> GetRunSignoffsAsync(SqlConnection connection, int runId, int? currentUserId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
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
ORDER BY CASE ISNULL(rs.SignoffRole, '')
            WHEN 'DataAnalyst' THEN 1
            WHEN 'Manager' THEN 2
            WHEN 'Director' THEN 3
            ELSE 4
         END,
         rs.SignedOffAt DESC;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@CurrentUserID", currentUserId.HasValue ? currentUserId.Value : DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync();
            var signoffs = new List<RunSignoffViewModel>();
            while (await reader.ReadAsync())
            {
                signoffs.Add(new RunSignoffViewModel
                {
                    Id = reader.GetInt32(0),
                    SignoffRole = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    ReviewerName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    ReviewerEmail = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Comment = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    SignedOffAt = reader.IsDBNull(5) ? DateTime.UtcNow : reader.GetDateTime(5),
                    IsCurrentUser = !reader.IsDBNull(6) && reader.GetBoolean(6)
                });
            }

            return signoffs;
        }

        private async Task<int?> GetSystemUserIdByEmailAsync(SqlConnection connection, string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            command.Parameters.AddWithValue("@Email", email);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private async Task<string?> GetEngagementRoleAsync(SqlConnection connection, int clientId, int userId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 EngagementRole
FROM dbo.UserClientAssignments
WHERE ClientID = @ClientID
  AND UserID = @UserID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@UserID", userId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }
        private static async Task<bool> IsWorkspaceSavedAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT CASE
    WHEN EXISTS (
        SELECT 1
        FROM dbo.ValidationRuns
        WHERE RunID = @RunID
          AND (
                WorkspaceSavedAt IS NOT NULL
                OR EXISTS (
                    SELECT 1
                    FROM dbo.ReviewSignoffs rs
                    WHERE rs.RunID = ValidationRuns.RunID
                      AND rs.SignoffRole = 'DataAnalyst'
                )
          )
    ) THEN 1
    ELSE 0
END;";
            command.Parameters.AddWithValue("@RunID", runId);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }
        private async Task<bool> HasSignoffRoleAsync(SqlConnection connection, int runId, string signoffRole)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT COUNT(1)
FROM dbo.ReviewSignoffs
WHERE RunID = @RunID
  AND SignoffRole = @SignoffRole;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@SignoffRole", signoffRole);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }

        private async Task<string?> GetValidationRecordHashAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 RecordHash
FROM dbo.ValidationRuns
WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private async Task<string?> GetLatestValidationHashAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 RecordHash
FROM dbo.ValidationRuns
WHERE ClientID = @ClientID
  AND RuleNumber = @RuleNumber
  AND RecordHash IS NOT NULL
ORDER BY RunTimestamp DESC, RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        private async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 Status
FROM dbo.Clients
WHERE ClientID = @ClientID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            var status = Convert.ToString(await command.ExecuteScalarAsync());
            if (string.Equals(status, "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Archived engagements are read-only.");
        }

        private async Task<SqlConnection> OpenSystemConnectionAsync()
        {
            var server = _configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = _configuration["SystemDatabase:Name"] ?? "HEMISBaseSystem";
            var trust = _configuration.GetValue("SystemDatabase:TrustServerCertificate", true);

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                IntegratedSecurity = true,
                TrustServerCertificate = trust,
                Encrypt = false,
                ConnectTimeout = 180
            };

            var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<ValidationSummary>(decoded);
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyBrowserPreview(ValidationSummary summary)
        {
            summary.ValidationRows = summary.ValidationRows
                .Take(BrowserPreviewRowLimit)
                .ToList();
            summary.Exceptions = summary.Exceptions
                .Take(BrowserPreviewRowLimit)
                .ToList();
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);

        private static string Sanitise(string name) =>
            name.Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();
    }
}


