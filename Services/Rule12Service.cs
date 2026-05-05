using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class Rule12Service : IRule12Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule12Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
        {
            _configuration = configuration;
            _pendingValidationCache = pendingValidationCache;
        }

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
                while (await reader.ReadAsync())
                    items.Add(reader.GetString(0));

                return new DatabaseListResult { Success = true, Databases = items };
            }
            catch (Exception ex)
            {
                return new DatabaseListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule12TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
        {
            try
            {
                var connStr = BuildConnectionString(server, database, driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME;";
                await using var reader = await cmd.ExecuteReaderAsync();

                var tables = new List<string>();
                while (await reader.ReadAsync())
                    tables.Add(reader.GetString(0));

                return new Rule12TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoCregTable = FindFirst(tables, ["dbo_CREG", "CREG"], ["creg"]),
                    AutoCrseTable = FindFirst(tables, ["dbo_CRSE", "CRSE"], ["crse"])
                };
            }
            catch (Exception ex)
            {
                return new Rule12TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule12VerifyResult> VerifyTablesAsync(Rule12VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.CregTable, request.CrseTable, request.CrseTable);

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var cregTable = Sanitise(request.CregTable);
                var crseTable = Sanitise(request.CrseTable);

                var cregCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{cregTable}];");
                var crseCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{crseTable}];");

                await using var command = conn.CreateConfiguredCommand();
                command.CommandText = BuildPopulationCountSql(cregTable, crseTable);
                await using var reader = await command.ExecuteReaderAsync();

                var result = new Rule12VerifyResult
                {
                    Success = true,
                    CregRecordCount = cregCount,
                    CrseRecordCount = crseCount
                };

                if (await reader.ReadAsync())
                {
                    result.TotalCoursesSelected = GetInt(reader, 0);
                    result.MatchedCourseCount = GetInt(reader, 1);
                    result.MissingCourseCount = GetInt(reader, 2);
                }

                return result;
            }
            catch (Exception ex)
            {
                return new Rule12VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule12ValidationSummary> RunValidationAsync(Rule12ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.CregTable, request.CrseTable, request.CrseTable);

                var browserSummary = await AnalyseAsync(request, includeAllReviewRows: false);
                if (browserSummary.Success && request.ClientId > 0)
                {
                    try
                    {
                        var summaryToPersist = CloneSummary(browserSummary);
                        if (summaryToPersist.IsPreviewOnly || summaryToPersist.ReviewRows.Count < summaryToPersist.TotalValidated)
                            summaryToPersist = await AnalyseAsync(request, includeAllReviewRows: true);

                        summaryToPersist.SavedRunId = null;
                        browserSummary.SavedRunId = await SaveValidationRunAsync(
                            CloneValidationRequest(request),
                            summaryToPersist,
                            userEmail,
                            userName,
                            markWorkspaceSaved: false);

                        if (!string.IsNullOrWhiteSpace(userEmail))
                            _pendingValidationCache.ClearPending(12, request.ClientId, userEmail!);
                    }
                    catch (Exception ex)
                    {
                        browserSummary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                if (!browserSummary.SavedRunId.HasValue)
                {
                    if (browserSummary.Success && request.ClientId > 0 && !string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.StorePending(12, request.ClientId, userEmail!, request, CloneSummary(browserSummary), userName);

                    browserSummary.Warning = string.IsNullOrWhiteSpace(browserSummary.Warning)
                        ? "Counts reflect the full dbo_CREG course population result set. Browser review rows are limited for performance."
                        : browserSummary.Warning;
                }
                else
                {
                    browserSummary.Warning = "The current Rule 12 run has been written to the system database. Click Save Workspace to finalize it for signoff.";
                }

                ApplyBrowserPreview(browserSummary);
                return browserSummary;
            }
            catch (Exception ex)
            {
                return new Rule12ValidationSummary
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule12ValidationSummary> GetExportSummaryAsync(Rule12ValidationRequest request)
        {
            ValidateRequest(request);
            await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.CregTable, request.CrseTable, request.CrseTable);
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public Task<Rule12ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail)
        {
            var pending = _pendingValidationCache.GetPending<Rule12ValidationRequest, Rule12ValidationSummary>(12, clientId, reviewerEmail);
            if (pending == null)
                return Task.FromResult<Rule12ValidationSummary?>(null);

            var preview = CloneSummary(pending.Summary);
            preview.SavedRunId = null;
            preview.Warning = "This Rule 12 validation is still pending. Click Save Workspace to write it to the system database.";
            ApplyBrowserPreview(preview);
            return Task.FromResult<Rule12ValidationSummary?>(preview);
        }

        public Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail)
            => Task.FromResult(_pendingValidationCache.HasPending(12, clientId, reviewerEmail));

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule12WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID,
    vr.ClientID,
    ISNULL(vr.HemisServer, '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable, '') AS CregTable,
    ISNULL(vr.DeceasedTable, '') AS CrseTable,
    ISNULL(vr.StudColumn, '') AS LegacyCrseTable,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 12
  AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var runId = reader.GetInt32(0);
            var workspaceClientId = reader.GetInt32(1);
            var server = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var database = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var cregTable = reader.IsDBNull(4) ? "dbo_CREG" : reader.GetString(4);
            var crseTable = reader.IsDBNull(5) ? (reader.IsDBNull(6) ? "dbo_CRSE" : reader.GetString(6)) : reader.GetString(5);
            var currentStatus = reader.IsDBNull(7) ? "" : reader.GetString(7);
            var lastEditedByUserName = reader.IsDBNull(8) ? null : reader.GetString(8);
            DateTime? lastEditedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9);
            var encodedSummary = reader.IsDBNull(10) ? null : reader.GetString(10);

            await reader.CloseAsync();

            var deserializedSummary = DeserializeSummary(encodedSummary);
            if (deserializedSummary != null && includeSummary)
            {
                deserializedSummary = await ExpandAndPersistSavedSummaryIfNeededAsync(connection, runId, deserializedSummary, server);
                ApplyBrowserPreview(deserializedSummary);
            }
            var summary = includeSummary ? deserializedSummary : null;

            var workspace = new Rule12WorkspaceStateViewModel
            {
                ClientId = workspaceClientId,
                RunId = runId,
                Server = server,
                Database = database,
                CregTable = cregTable,
                CrseTable = crseTable,
                CurrentStatus = currentStatus,
                LastEditedByUserName = lastEditedByUserName,
                LastEditedAt = lastEditedAt,
                Summary = summary
            };

            if (summary != null)
                workspace.CurrentStatus = summary.Status;

            workspace.Driver = "ODBC Driver 17 for SQL Server";
            workspace.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId!.Value, currentUserId);
            workspace.HasDataAnalystSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            var currentRoleSignoff = signoffs.FirstOrDefault(s =>
                HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff = currentRoleSignoff != null;
            workspace.CurrentUserSignoffComment = currentRoleSignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved = await IsWorkspaceSavedAsync(connection, workspace.RunId!.Value);

            if (workspace.Summary != null)
                workspace.Summary.SavedRunId = workspace.RunId;

            return workspace;
        }

        public async Task<Rule12RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 12;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var savedRunId = reader.GetInt32(0);
            var clientId = reader.GetInt32(1);
            var isCurrentRun = !reader.IsDBNull(2) && reader.GetBoolean(2);
            var engagementName = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var maconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var sourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var encodedSummary = reader.IsDBNull(6) ? null : reader.GetString(6);
            var summary = DeserializeSummary(encodedSummary);
            if (summary == null)
                return null;

            summary.ClientId = clientId;
            if (summary.SavedRunId.GetValueOrDefault() <= 0)
                summary.SavedRunId = runId;

            await reader.CloseAsync();

            summary = await ExpandAndPersistSavedSummaryIfNeededAsync(connection, runId, summary, sourceServer);

            if (includeFullResults)
            {
                summary.DisplayedCount = summary.ReviewRows.Count;
                summary.IsPreviewOnly = false;
                summary.PreviewLimit = 0;
            }
            else
            {
                ApplyBrowserPreview(summary);
            }

            var review = new Rule12RunReviewViewModel
            {
                RunId = savedRunId,
                ClientId = clientId,
                IsCurrentRun = isCurrentRun,
                EngagementName = engagementName,
                MaconomyNumber = maconomyNumber,
                SourceServer = sourceServer,
                Summary = summary
            };

            review.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";
            review.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            review.HasDataAnalystSignoff = review.Signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            return review;
        }

        public async Task<Rule12WorkspaceSaveResult> SaveWorkspaceAsync(Rule12ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                ValidateRequest(request);

                if (request.RunId.HasValue && request.RunId.Value > 0)
                {
                    await using var connection = await OpenSystemConnectionAsync();
                    var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                    if (!clientId.HasValue || clientId.Value != request.ClientId)
                    {
                        return new Rule12WorkspaceSaveResult
                        {
                            Success = false,
                            Error = "The saved workspace could not be found for this engagement."
                        };
                    }

                    await EnsureClientNotArchivedAsync(connection, request.ClientId);

                    var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                    var previousHash = await GetValidationRecordHashAsync(connection, request.RunId.Value);

                    await using var command = connection.CreateConfiguredCommand();
                    command.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    WorkspaceSavedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID
  AND ClientID = @ClientID;";
                    command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                    command.Parameters.AddWithValue("@ClientID", request.ClientId);
                    command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                    command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                    command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule12|{request.RunId.Value}|{request.ClientId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                    await command.ExecuteNonQueryAsync();

                    if (!string.IsNullOrWhiteSpace(reviewerEmail))
                        _pendingValidationCache.ClearPending(12, request.ClientId, reviewerEmail);

                    var currentWorkspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                    return new Rule12WorkspaceSaveResult
                    {
                        Success = true,
                        Message = clearedSignoffs > 0
                            ? "Workspace saved. Existing signoffs were removed and the run must be reviewed again."
                            : "Workspace saved and marked for review again.",
                        SignoffsCleared = clearedSignoffs > 0,
                        ClearedSignoffCount = clearedSignoffs,
                        Workspace = currentWorkspace
                    };
                }

                var pending = _pendingValidationCache.GetPending<Rule12ValidationRequest, Rule12ValidationSummary>(12, request.ClientId, reviewerEmail);
                if (pending == null)
                {
                    return new Rule12WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run Rule 12 first so the current workspace is written to the system database."
                    };
                }

                if (!RequestsMatchForPendingSave(request, pending.Request))
                {
                    return new Rule12WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Workspace settings changed after validation. Run Rule 12 again before saving."
                    };
                }

                var summaryToSave = CloneSummary(pending.Summary);
                if (summaryToSave.IsPreviewOnly || summaryToSave.ReviewRows.Count < summaryToSave.TotalValidated)
                {
                    summaryToSave = await AnalyseAsync(pending.Request, includeAllReviewRows: true);
                }

                summaryToSave.SavedRunId = null;
                var requestToSave = CloneValidationRequest(pending.Request);

                var savedRunId = await SaveValidationRunAsync(requestToSave, summaryToSave, reviewerEmail, reviewerName ?? pending.ReviewerName, markWorkspaceSaved: true);
                _pendingValidationCache.ClearPending(12, request.ClientId, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule12WorkspaceSaveResult
                {
                    Success = true,
                    Message = $"Workspace saved as Run #{savedRunId}. Sign off this saved workspace when you are ready.",
                    SignoffsCleared = false,
                    ClearedSignoffCount = 0,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule12WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule12WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule12WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Saved workspace was not found."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash = await GetValidationRecordHashAsync(connection, runId);

                await using var markEdit = connection.CreateConfiguredCommand();
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
                markEdit.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule12|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                if (!string.IsNullOrWhiteSpace(reviewerEmail))
                    _pendingValidationCache.ClearPending(12, clientId.Value, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule12WorkspaceSaveResult
                {
                    Success = true,
                    Message = clearedSignoffs > 0
                        ? "Editing has begun. Existing signoffs were removed."
                        : "Editing has begun. Save the workspace when you are ready.",
                    SignoffsCleared = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule12WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue)
                throw new InvalidOperationException("The reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("The selected Rule 12 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            var signoffRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(signoffRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can sign off a Rule 12 run.");

            if (!string.Equals(signoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) &&
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
            command.Parameters.AddWithValue("@SignoffRole", signoffRole!);
            command.Parameters.AddWithValue("@Comment", string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim());
            await command.ExecuteNonQueryAsync();

            await UpdateRunStatusFromSignoffsAsync(connection, runId);
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue)
                throw new InvalidOperationException("The reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("The selected Rule 12 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0)
                return;
        }

        public Task<string> GenerateSqlAsync(Rule12ValidationRequest request)
        {
            ValidateRequest(request);

            var cregTable = Sanitise(request.CregTable);
            var crseTable = Sanitise(request.CrseTable);

            var sql = $@"-- HEMIS RULE 12: COURSE SELECTION FROM dbo_CREG
-- Linkage: [{cregTable}]._030 = [{crseTable}]._030
-- Rule: test 100% of the course population selected from dbo_CREG
-- PASS when a matching course exists on dbo_CRSE._030

WITH SelectedCourses AS
(
    SELECT DISTINCT
        CAST(CREG.[_030] AS nvarchar(255)) AS CREG__030
    FROM [{cregTable}] CREG
    WHERE CREG.[_030] IS NOT NULL
),
MatchedCourses AS
(
    SELECT
        Selected.CREG__030,
        CAST(CRSE.[_030] AS nvarchar(255)) AS CRSE__030
    FROM SelectedCourses Selected
    LEFT JOIN [{crseTable}] CRSE
        ON CAST(CRSE.[_030] AS nvarchar(255)) = Selected.CREG__030
)
SELECT
    'Control_1' AS Control_Type,
    'CONTROL 1: dbo_CREG._030 = dbo_CRSE._030' AS Control_Label,
    CREG__030,
    CRSE__030,
    CASE WHEN CRSE__030 IS NOT NULL THEN 'PASS' ELSE 'FAIL' END AS Validation_Result
FROM MatchedCourses
ORDER BY CREG__030;

SELECT
    (SELECT COUNT(*) FROM SelectedCourses) AS Selected_Courses,
    (SELECT COUNT(*) FROM MatchedCourses WHERE CRSE__030 IS NOT NULL) AS Matched_Courses,
    (SELECT COUNT(*) FROM MatchedCourses WHERE CRSE__030 IS NULL) AS Missing_Courses;";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule12ValidationSummary> AnalyseAsync(Rule12ValidationRequest request, bool includeAllReviewRows)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var cregTable = Sanitise(request.CregTable);
            var crseTable = Sanitise(request.CrseTable);

            var cregCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{cregTable}];");
            var crseCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{crseTable}];");

            await using var countCommand = conn.CreateConfiguredCommand();
            countCommand.CommandText = BuildPopulationCountSql(cregTable, crseTable);
            await using var countReader = await countCommand.ExecuteReaderAsync();

            var selectedCourseCount = 0;
            var matchedCourseCount = 0;
            var missingCourseCount = 0;
            if (await countReader.ReadAsync())
            {
                selectedCourseCount = GetInt(countReader, 0);
                matchedCourseCount = GetInt(countReader, 1);
                missingCourseCount = GetInt(countReader, 2);
            }

            await countReader.CloseAsync();

            var reviewRows = await LoadControlRowsAsync(conn, cregTable, crseTable, includeAllReviewRows ? null : BrowserPreviewRowLimit);
            reviewRows = NormalizeReviewRows(reviewRows);

            var controlSummaries = BuildControlSummaries(selectedCourseCount, matchedCourseCount);
            var totalValidated = controlSummaries.Sum(x => x.TotalCount);
            var passCount = controlSummaries.Sum(x => x.PassCount);
            var failCount = controlSummaries.Sum(x => x.FailCount);
            var isPreviewOnly = !includeAllReviewRows && totalValidated > reviewRows.Count;

            return new Rule12ValidationSummary
            {
                Success = true,
                CregRecordCount = cregCount,
                CrseRecordCount = crseCount,
                TotalRequested = totalValidated,
                TotalValidated = totalValidated,
                DisplayedCount = reviewRows.Count,
                IsPreviewOnly = isPreviewOnly,
                PreviewLimit = isPreviewOnly ? BrowserPreviewRowLimit : 0,
                PassCount = passCount,
                FailCount = failCount,
                ExceptionRate = totalValidated == 0 ? 0m : Math.Round(failCount * 100m / totalValidated, 2),
                Status = failCount == 0 ? "PASS" : "FAIL",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                CregTable = request.CregTable,
                CrseTable = request.CrseTable,
                TableLinkageText = $"{request.CregTable}._030 = {request.CrseTable}._030",
                RuleModeText = "100% population testing of dbo_CREG course codes against dbo_CRSE",
                ProcedureSteps = BuildProcedureSteps(request.CregTable, request.CrseTable),
                ClientId = request.ClientId,
                ControlSummaries = controlSummaries,
                ReviewRows = reviewRows,
                Warning = includeAllReviewRows
                    ? "Rule 12 completed with the full dbo_CREG course population result set."
                    : "Counts reflect the full dbo_CREG course population result set. Browser review rows are limited for performance."
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule12ValidationRequest request, Rule12ValidationSummary summary, string? userEmail, string? userName, bool markWorkspaceSaved)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 12);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 12);
            var failRows = summary.ReviewRows.Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).ToList();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
INSERT INTO dbo.ValidationRuns
(
    ClientID, UserID, RuleNumber, RuleName, Status, TotalRecords, PassCount, FailCount, ExceptionRate, RunTimestamp,
    HemisServer, AuditDatabase, StudTable, DeceasedTable, StudColumn, DeceasedColumn,
    ExceptionsJSON, ResultsJSON, RunByUserName, LastEditedByUserName, LastEditedAt, PreviousHash, RecordHash, WorkspaceSavedAt, IsCurrent
)
VALUES
(
    @ClientID, @UserID, 12, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @BridgeTable, @CrseTable, NULL,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, @WorkspaceSavedAt, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "Course Selection from dbo_CREG");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.CregTable);
            command.Parameters.AddWithValue("@BridgeTable", request.CrseTable);
            command.Parameters.AddWithValue("@CrseTable", request.CrseTable);
            var persistedSummary = CloneSummary(summary);
            persistedSummary.SavedRunId = summary.SavedRunId;
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(failRows)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persistedSummary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
            command.Parameters.AddWithValue("@WorkspaceSavedAt", markWorkspaceSaved ? DateTime.UtcNow : (object)DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCommand = connection.CreateConfiguredCommand();
            hashCommand.CommandText = @"
UPDATE dbo.ValidationRuns
SET RecordHash = @RecordHash
WHERE RunID = @RunID;";
            hashCommand.Parameters.AddWithValue("@RunID", runId);
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule12|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            await UpdateStoredSummaryAsync(connection, runId, summary);

            return runId;
        }

        private static string BuildPopulationCountSql(string cregTable, string crseTable) => $@"
{BuildRule12SourceCtes(cregTable, crseTable)}
SELECT
    COUNT(1) AS SelectedCourseCount,
    SUM(CASE WHEN CRSE__030 IS NOT NULL THEN 1 ELSE 0 END) AS MatchedCourseCount,
    SUM(CASE WHEN CRSE__030 IS NULL THEN 1 ELSE 0 END) AS MissingCourseCount
FROM CourseSelectionResults;";

        private async Task<List<Rule12ValidationRowRecord>> LoadControlRowsAsync(SqlConnection connection, string cregTable, string crseTable, int? maxRows)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = BuildAllControlsSql(cregTable, crseTable, maxRows);

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule12ValidationRowRecord>();
            while (await reader.ReadAsync())
            {
                var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    displayValues[reader.GetName(i)] = reader.IsDBNull(i)
                        ? null
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                }

                rows.Add(new Rule12ValidationRowRecord
                {
                    ValidationNumber = rows.Count + 1,
                    ControlType = ReadValue(displayValues, "Control_Type"),
                    ControlLabel = ReadValue(displayValues, "Control_Label"),
                    ValidationResult = ReadValue(displayValues, "Validation_Result"),
                    ValidationExplanation = ReadValue(displayValues, "Validation_Explanation"),
                    DisplayValues = displayValues
                });

                EnrichRule12DisplayValues(rows[^1]);
            }

            return rows;
        }

        private static string BuildAllControlsSql(string cregTable, string crseTable, int? maxRows)
        {
            var topClause = maxRows.HasValue && maxRows.Value > 0 ? $"TOP {maxRows.Value}" : string.Empty;

            return $@"
{BuildRule12SourceCtes(cregTable, crseTable)}
SELECT {topClause}
    1 AS Control_Sort,
    'Control_1' AS Control_Type,
    'CONTROL 1: dbo_CREG._030 = dbo_CRSE._030' AS Control_Label,
    CASE
        WHEN ResultRows.CRSE__030 IS NOT NULL THEN 'PASS' ELSE 'FAIL'
    END AS Validation_Result,
    CASE
        WHEN ResultRows.CRSE__030 IS NOT NULL
            THEN 'Matched dbo_CREG course to dbo_CRSE.'
        ELSE 'dbo_CREG course has no matching dbo_CRSE course.'
    END AS Validation_Explanation,
    ResultRows.CREG__030,
    ResultRows.CRSE__030
FROM CourseSelectionResults ResultRows
ORDER BY ResultRows.CREG__030;";
        }

        private static string BuildRule12SourceCtes(string cregTable, string crseTable) => $@"
WITH SelectedCourses AS
(
    SELECT DISTINCT
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CREG.[_030])))) AS CREG__030
    FROM [{cregTable}] CREG
    WHERE CREG.[_030] IS NOT NULL
      AND LTRIM(RTRIM(CONVERT(nvarchar(255), CREG.[_030]))) <> ''
),
ReferenceCourses AS
(
    SELECT DISTINCT
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CRSE.[_030])))) AS CRSE__030
    FROM [{crseTable}] CRSE
    WHERE CRSE.[_030] IS NOT NULL
      AND LTRIM(RTRIM(CONVERT(nvarchar(255), CRSE.[_030]))) <> ''
),
CourseSelectionResults AS
(
    SELECT
        Selected.CREG__030,
        Reference.CRSE__030
    FROM SelectedCourses Selected
    LEFT JOIN ReferenceCourses Reference
        ON Reference.CRSE__030 = Selected.CREG__030
)";

        private static List<Rule12ControlSummaryItemViewModel> BuildControlSummaries(
            int selectedCourseCount,
            int matchedCourseCount)
        {
            return new List<Rule12ControlSummaryItemViewModel>
            {
                BuildControlSummary(
                    "Control_1",
                    "Control 1",
                    "dbo_CREG._030 = dbo_CRSE._030",
                    selectedCourseCount,
                    matchedCourseCount)
            };
        }

        private static Rule12ControlSummaryItemViewModel BuildControlSummary(
            string controlType,
            string controlLabel,
            string criteriaText,
            int totalCount,
            int passCount)
        {
            var failCount = Math.Max(totalCount - passCount, 0);
            return new Rule12ControlSummaryItemViewModel
            {
                ControlType = controlType,
                ControlLabel = controlLabel,
                CriteriaText = criteriaText,
                RequestedCount = totalCount,
                AvailableCount = totalCount,
                AchievedCount = totalCount,
                TotalCount = totalCount,
                PassCount = passCount,
                FailCount = failCount,
                Status = failCount == 0 ? "PASS" : "FAIL"
            };
        }

        private static List<Rule12ValidationRowRecord> NormalizeReviewRows(IEnumerable<Rule12ValidationRowRecord>? rows)
        {
            var normalized = (rows ?? Enumerable.Empty<Rule12ValidationRowRecord>())
                .Select((row, index) =>
                {
                    row.ValidationNumber = index + 1;
                    return row;
                })
                .ToList();

            return normalized;
        }

        private async Task<Rule12ValidationSummary> ExpandSavedSummaryIfNeededAsync(Rule12ValidationSummary summary, string? server)
        {
            if (!summary.IsPreviewOnly &&
                summary.ReviewRows.Count >= summary.TotalValidated)
            {
                return summary;
            }

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(summary.Database) ||
                string.IsNullOrWhiteSpace(summary.CregTable) ||
                string.IsNullOrWhiteSpace(summary.CrseTable))
            {
                return summary;
            }

            try
            {
                var expanded = await AnalyseAsync(
                    new Rule12ValidationRequest
                    {
                        ClientId = summary.ClientId,
                        RunId = summary.SavedRunId,
                        Server = server,
                        Database = summary.Database,
                        Driver = "ODBC Driver 17 for SQL Server",
                        CregTable = summary.CregTable,
                        CrseTable = summary.CrseTable
                    },
                    includeAllReviewRows: true);

                expanded.Timestamp = string.IsNullOrWhiteSpace(summary.Timestamp) ? expanded.Timestamp : summary.Timestamp;
                expanded.ClientId = summary.ClientId;
                expanded.SavedRunId = summary.SavedRunId;
                expanded.Warning = string.IsNullOrWhiteSpace(summary.Warning)
                    ? "Saved Rule 12 results were expanded from the stored browser preview to the full result set."
                    : $"{summary.Warning} Full saved results were reloaded from the saved Rule 12 configuration.";

                return expanded;
            }
            catch
            {
                return summary;
            }
        }

        private async Task<Rule12ValidationSummary> ExpandAndPersistSavedSummaryIfNeededAsync(SqlConnection connection, int runId, Rule12ValidationSummary summary, string? server)
        {
            var expanded = await ExpandSavedSummaryIfNeededAsync(summary, server);
            if (!ReferenceEquals(expanded, summary))
            {
                expanded.SavedRunId = runId;
                await UpdateStoredSummaryAsync(connection, runId, expanded);
            }

            return expanded;
        }

        private static Rule12ValidationSummary CloneSummary(Rule12ValidationSummary summary)
        {
            return new Rule12ValidationSummary
            {
                Success = summary.Success,
                CregRecordCount = summary.CregRecordCount,
                CrseRecordCount = summary.CrseRecordCount,
                TotalRequested = summary.TotalRequested,
                TotalValidated = summary.TotalValidated,
                DisplayedCount = summary.DisplayedCount,
                IsPreviewOnly = summary.IsPreviewOnly,
                PreviewLimit = summary.PreviewLimit,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                CregTable = summary.CregTable,
                CrseTable = summary.CrseTable,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText = summary.RuleModeText,
                ProcedureSteps = summary.ProcedureSteps.ToList(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries
                    .Select(item => new Rule12ControlSummaryItemViewModel
                    {
                        ControlType = item.ControlType,
                        ControlLabel = item.ControlLabel,
                        CriteriaText = item.CriteriaText,
                        RequestedCount = item.RequestedCount,
                        AvailableCount = item.AvailableCount,
                        AchievedCount = item.AchievedCount,
                        TotalCount = item.TotalCount,
                        PassCount = item.PassCount,
                        FailCount = item.FailCount,
                        Status = item.Status
                    })
                    .ToList(),
                ReviewRows = summary.ReviewRows
                    .Select(CloneReviewRow)
                    .ToList(),
                Warning = summary.Warning,
                Error = summary.Error
            };
        }

        private static Rule12ValidationRowRecord CloneReviewRow(Rule12ValidationRowRecord row)
        {
            return new Rule12ValidationRowRecord
            {
                ValidationNumber = row.ValidationNumber,
                ControlType = row.ControlType,
                ControlLabel = row.ControlLabel,
                ValidationResult = row.ValidationResult,
                ValidationExplanation = row.ValidationExplanation,
                DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static Rule12ValidationSummary CreateBrowserPreview(Rule12ValidationSummary summary)
        {
            var controlGroups = summary.ReviewRows
                .GroupBy(row => row.ControlType, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => GetControlSort(group.Key))
                .ToList();

            var controlCount = Math.Max(controlGroups.Count, 1);
            var baseLimit = Math.Max(BrowserPreviewRowLimit / controlCount, 1);
            var remainder = Math.Max(BrowserPreviewRowLimit - (baseLimit * controlGroups.Count), 0);
            var previewRows = controlGroups
                .SelectMany((group, index) => TakePreviewRowsForControl(
                    group.OrderBy(row => row.ValidationNumber).ToList(),
                    baseLimit + (index < remainder ? 1 : 0)))
                .Take(BrowserPreviewRowLimit)
                .ToList();

            return new Rule12ValidationSummary
            {
                Success = summary.Success,
                CregRecordCount = summary.CregRecordCount,
                CrseRecordCount = summary.CrseRecordCount,
                TotalRequested = summary.TotalRequested,
                TotalValidated = summary.TotalValidated,
                DisplayedCount = previewRows.Count,
                IsPreviewOnly = summary.TotalValidated > previewRows.Count,
                PreviewLimit = summary.TotalValidated > previewRows.Count ? previewRows.Count : 0,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                CregTable = summary.CregTable,
                CrseTable = summary.CrseTable,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText = summary.RuleModeText,
                ProcedureSteps = summary.ProcedureSteps.ToList(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries
                    .Select(item => new Rule12ControlSummaryItemViewModel
                    {
                        ControlType = item.ControlType,
                        ControlLabel = item.ControlLabel,
                        CriteriaText = item.CriteriaText,
                        RequestedCount = item.RequestedCount,
                        AvailableCount = item.AvailableCount,
                        AchievedCount = item.AchievedCount,
                        TotalCount = item.TotalCount,
                        PassCount = item.PassCount,
                        FailCount = item.FailCount,
                        Status = item.Status
                    })
                    .ToList(),
                ReviewRows = previewRows,
                Warning = summary.Warning,
                Error = summary.Error
            };
        }

        private static List<Rule12ValidationRowRecord> TakePreviewRowsForControl(
            IReadOnlyList<Rule12ValidationRowRecord> rows,
            int limit)
        {
            if (limit <= 0 || rows.Count == 0)
                return new List<Rule12ValidationRowRecord>();

            var failRows = rows
                .Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var passRows = rows
                .Where(row => !string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var selected = new List<Rule12ValidationRowRecord>(limit);
            var failTake = failRows.Count == 0
                ? 0
                : Math.Min(failRows.Count, Math.Max(1, limit / 2));

            selected.AddRange(failRows.Take(failTake));

            var passTake = Math.Min(passRows.Count, limit - selected.Count);
            selected.AddRange(passRows.Take(passTake));

            if (selected.Count < limit)
                selected.AddRange(failRows.Skip(failTake).Take(limit - selected.Count));

            if (selected.Count < limit)
                selected.AddRange(passRows.Skip(passTake).Take(limit - selected.Count));

            return selected
                .OrderBy(row => row.ValidationNumber)
                .ToList();
        }

        private static void ApplyBrowserPreview(Rule12ValidationSummary summary)
        {
            var preview = CreateBrowserPreview(summary);
            summary.DisplayedCount = preview.DisplayedCount;
            summary.IsPreviewOnly = preview.IsPreviewOnly;
            summary.PreviewLimit = preview.PreviewLimit;
            summary.ReviewRows = preview.ReviewRows;
        }

        private static Rule12ValidationRequest CloneValidationRequest(Rule12ValidationRequest request)
        {
            return new Rule12ValidationRequest
            {
                ClientId = request.ClientId,
                RunId = request.RunId,
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                CregTable = request.CregTable,
                CrseTable = request.CrseTable
            };
        }

        private static bool RequestsMatchForPendingSave(Rule12ValidationRequest current, Rule12ValidationRequest pending)
        {
            return current.ClientId == pending.ClientId &&
                   string.Equals(current.Server?.Trim(), pending.Server?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Database?.Trim(), pending.Database?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Driver?.Trim(), pending.Driver?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.CregTable?.Trim(), pending.CregTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.CrseTable?.Trim(), pending.CrseTable?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static int GetControlSort(string? controlType) => controlType switch
        {
            "Control_1" => 1,
            _ => 99
        };

        private static List<string> BuildProcedureSteps(string cregTable, string crseTable) =>
            new()
            {
                $"From the {cregTable} table, select courses from _030.",
                $"Keep the full distinct {cregTable}._030 population; no sampling is applied.",
                $"Test whether each selected course has a matching {crseTable}._030 value.",
                "Mark rows PASS when the course exists in dbo_CRSE and FAIL when it does not.",
                "Return the full course population selected from dbo_CREG."
            };

        private async Task EnsureColumnsExistAsync(string server, string database, string driver, string cregTable, string crseTable, string _)
        {
            var cregColumns = await GetTableColumnsAsync(server, database, driver, cregTable);
            var crseColumns = await GetTableColumnsAsync(server, database, driver, crseTable);

            EnsureHasColumns(cregTable, cregColumns, "_030");
            EnsureHasColumns(crseTable, crseColumns, "_030");
        }

        private async Task<List<string>> GetTableColumnsAsync(string server, string database, string driver, string tableName)
        {
            ValidateObjectName(tableName);

            var connStr = BuildConnectionString(server, database, driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = @TableName
ORDER BY ORDINAL_POSITION;";
            cmd.Parameters.AddWithValue("@TableName", Sanitise(tableName));

            await using var reader = await cmd.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                    columns.Add(reader.GetString(0));
            }

            return columns;
        }

        private static void EnsureHasColumns(string tableName, IReadOnlyCollection<string> availableColumns, params string[] requiredColumns)
        {
            var missing = requiredColumns
                .Where(required => !availableColumns.Contains(required, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (missing.Count > 0)
                throw new InvalidOperationException($"Table {tableName} is missing required column(s): {string.Join(", ", missing)}.");
        }

        private static void ValidateRequest(Rule12ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.CregTable))
                throw new InvalidOperationException("CREG table is required.");
            if (string.IsNullOrWhiteSpace(request.CrseTable))
                throw new InvalidOperationException("CRSE table is required.");

            ValidateObjectName(request.CregTable);
            ValidateObjectName(request.CrseTable);
        }

        private static void ValidateRequest(Rule12VerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.CregTable))
                throw new InvalidOperationException("CREG table is required.");
            if (string.IsNullOrWhiteSpace(request.CrseTable))
                throw new InvalidOperationException("CRSE table is required.");

            ValidateObjectName(request.CregTable);
            ValidateObjectName(request.CrseTable);
        }

        private static void ValidateObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Table or column name is required.");

            foreach (var bad in new[] { ";", "'", "\"", "--", "/*", "*/" })
            {
                if (value.Contains(bad, StringComparison.Ordinal))
                    throw new InvalidOperationException("Unsafe table or column name was provided.");
            }
        }

        private static string? FindFirst(IEnumerable<string> values, string[] exactMatches, string[] containsMatches)
        {
            foreach (var exact in exactMatches)
            {
                var match = values.FirstOrDefault(c => c.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            foreach (var fragment in containsMatches)
            {
                var match = values.FirstOrDefault(c => c.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            return values.FirstOrDefault();
        }

        private async Task<int> ClearSignoffsAndFlagForReviewAsync(SqlConnection connection, int runId)
        {
            await using var countCommand = connection.CreateConfiguredCommand();
            countCommand.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            countCommand.Parameters.AddWithValue("@RunID", runId);
            var existingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            await using var deleteCommand = connection.CreateConfiguredCommand();
            deleteCommand.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            deleteCommand.Parameters.AddWithValue("@RunID", runId);
            await deleteCommand.ExecuteNonQueryAsync();

            await using var updateCommand = connection.CreateConfiguredCommand();
            updateCommand.CommandText = "UPDATE dbo.ValidationRuns SET Status = 'Needs Review' WHERE RunID = @RunID;";
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
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'DataAnalyst') THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Manager') THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Director') THEN 1 ELSE 0 END;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return false;

            return reader.GetInt32(0) == 1 && reader.GetInt32(1) == 1 && reader.GetInt32(2) == 1;
        }

        private async Task SetRunStatusAsync(SqlConnection connection, int runId, string status)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET Status = @Status WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@Status", status);
            await command.ExecuteNonQueryAsync();
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
SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM dbo.ReviewSignoffs
    WHERE RunID = @RunID
      AND SignoffRole = @SignoffRole
) THEN 1 ELSE 0 END;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@SignoffRole", signoffRole);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }

        private async Task<string?> GetValidationRecordHashAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE RunID = @RunID;";
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

        private async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 Status FROM dbo.Clients WHERE ClientID = @ClientID;";
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

        private static string BuildConnectionString(string server, string database, string driver) =>
            $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";

        private static string Sanitise(string name) =>
            name.Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();

        private static async Task<int> CountAsync(SqlConnection connection, string sql)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        private static Rule12ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                if (string.IsNullOrWhiteSpace(decoded))
                    return null;
                return JsonConvert.DeserializeObject<Rule12ValidationSummary>(decoded);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<int?> GetClientIdForRunAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private static async Task UpdateStoredSummaryAsync(SqlConnection connection, int runId, Rule12ValidationSummary summary)
        {
            await using var update = connection.CreateConfiguredCommand();
            update.CommandText = @"
UPDATE dbo.ValidationRuns
SET ResultsJSON = @ResultsJSON
WHERE RunID = @RunID;";
            update.Parameters.AddWithValue("@RunID", runId);
            update.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            await update.ExecuteNonQueryAsync();
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);

        private static void EnrichRule12DisplayValues(Rule12ValidationRowRecord row)
        {
            var values = row.DisplayValues;
            var validationResult = ReadValue(values, "Validation_Result");
            var isPass = string.Equals(validationResult, "PASS", StringComparison.OrdinalIgnoreCase);
            var creg030 = FormatRule12ColumnValue(ReadValue(values, "CREG__030"));
            var crse030 = FormatRule12ColumnValue(ReadValue(values, "CRSE__030"));

            const string criteriaText = "dbo_CREG._030 = dbo_CRSE._030";
            var validationExplanation = isPass
                ? $"Passed because dbo_CREG course '{creg030}' exists in dbo_CRSE as '{crse030}'."
                : $"Failed because dbo_CREG course '{creg030}' does not exist in dbo_CRSE.";
            var qualCriteriaMessage = $"Selected course code from dbo_CREG._030 = '{creg030}'.";
            var credLinkMessage = isPass
                ? $"Matched dbo_CRSE._030 = '{crse030}'."
                : "No matching dbo_CRSE._030 value was found.";
            var registrationMessage = isPass
                ? "Course exists in both tables."
                : "Course exists only in dbo_CREG.";
            var finalResultMessage = isPass
                ? "Passed this criteria because the selected dbo_CREG course exists in dbo_CRSE."
                : "Failed this criteria because the selected dbo_CREG course does not exist in dbo_CRSE.";

            values["FINAL_RULE_TEXT"] = criteriaText;
            values["Validation_Explanation"] = validationExplanation;
            values["CRSE_CRITERIA_MESSAGE"] = qualCriteriaMessage;
            values["CRSE_SELECTION_MESSAGE"] = credLinkMessage;
            values["CREG_LINK_MESSAGE"] = registrationMessage;
            values["FINAL_RESULT_MESSAGE"] = finalResultMessage;
            row.ValidationExplanation = validationExplanation;
        }

        private static string FormatRule12ColumnValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "[blank]" : value.Trim();

        private static int GetInt(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

        private static string ReadValue(IReadOnlyDictionary<string, string?> values, string key) =>
            values.TryGetValue(key, out var value) ? value ?? "" : "";

    }
}









