using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class Rule11Service : IRule11Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule11Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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

        public async Task<Rule11TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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

                return new Rule11TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoQualTable = FindFirst(
                        tables,
                        ["dbo_QUAL", "QUAL"],
                        ["qual"])
                };
            }
            catch (Exception ex)
            {
                return new Rule11TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule11VerifyResult> VerifyTablesAsync(Rule11VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.QualTable, request.QualTable, request.QualTable);

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var qualTable = Sanitise(request.QualTable);

                var qualCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{qualTable}];");

                await using var command = conn.CreateConfiguredCommand();
                command.CommandText = BuildPopulationCountSql(
                    qualTable,
                    BuildSqlStringList(NormalizePostgraduateTypes(request.PostgraduateTypesCsv)),
                    request.ExcludeMPrefixPattern);
                await using var reader = await command.ExecuteReaderAsync();

                var result = new Rule11VerifyResult
                {
                    Success = true,
                    QualRecordCount = qualCount
                };

                if (await reader.ReadAsync())
                {
                    result.ApprovedQualificationCount = GetInt(reader, 0);
                    result.UndergraduateCount = GetInt(reader, 1);
                    result.PostgraduateCount = GetInt(reader, 2);
                }

                return result;
            }
            catch (Exception ex)
            {
                return new Rule11VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule11ValidationSummary> RunValidationAsync(Rule11ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.QualTable, request.QualTable, request.QualTable);

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
                            _pendingValidationCache.ClearPending(11, request.ClientId, userEmail!);
                    }
                    catch (Exception ex)
                    {
                        browserSummary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                if (!browserSummary.SavedRunId.HasValue)
                {
                    if (browserSummary.Success && request.ClientId > 0 && !string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.StorePending(11, request.ClientId, userEmail!, request, CloneSummary(browserSummary), userName);

                    browserSummary.Warning = string.IsNullOrWhiteSpace(browserSummary.Warning)
                        ? "Counts reflect the full dbo_QUAL qualification population result set. Browser review rows are limited for performance."
                        : browserSummary.Warning;
                }
                else
                {
                    browserSummary.Warning = "The current Rule 11 run has been written to the system database. Click Save Workspace to finalize it for signoff.";
                }

                ApplyBrowserPreview(browserSummary);
                return browserSummary;
            }
            catch (Exception ex)
            {
                return new Rule11ValidationSummary
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule11ValidationSummary> GetExportSummaryAsync(Rule11ValidationRequest request)
        {
            ValidateRequest(request);
            await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.QualTable, request.QualTable, request.QualTable);
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public Task<Rule11ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail)
        {
            var pending = _pendingValidationCache.GetPending<Rule11ValidationRequest, Rule11ValidationSummary>(11, clientId, reviewerEmail);
            if (pending == null)
                return Task.FromResult<Rule11ValidationSummary?>(null);

            var preview = CloneSummary(pending.Summary);
            preview.SavedRunId = null;
            preview.Warning = "This Rule 11 validation is still pending. Click Save Workspace to write it to the system database.";
            ApplyBrowserPreview(preview);
            return Task.FromResult<Rule11ValidationSummary?>(preview);
        }

        public Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail)
            => Task.FromResult(_pendingValidationCache.HasPending(11, clientId, reviewerEmail));

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule11WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
    ISNULL(vr.StudTable, '') AS QualTable,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 11
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
            var qualTable = reader.IsDBNull(4) ? "dbo_QUAL" : reader.GetString(4);
            var currentStatus = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var lastEditedByUserName = reader.IsDBNull(6) ? null : reader.GetString(6);
            DateTime? lastEditedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7);
            var encodedSummary = reader.IsDBNull(8) ? null : reader.GetString(8);

            await reader.CloseAsync();

            var deserializedSummary = DeserializeSummary(encodedSummary);
            if (deserializedSummary != null && includeSummary)
            {
                deserializedSummary = await ExpandAndPersistSavedSummaryIfNeededAsync(connection, runId, deserializedSummary, server);
                ApplyBrowserPreview(deserializedSummary);
            }
            var summary = includeSummary ? deserializedSummary : null;

            var workspace = new Rule11WorkspaceStateViewModel
            {
                ClientId = workspaceClientId,
                RunId = runId,
                Server = server,
                Database = database,
                QualTable = qualTable,
                ExcludeMPrefixPattern = deserializedSummary?.ExcludeMPrefixPattern ?? true,
                PostgraduateTypesCsv = deserializedSummary?.PostgraduateTypesCsv ?? "07,27,28,49,72,73,08,30,50,74,75",
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

        public async Task<Rule11RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 11;";
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

            var review = new Rule11RunReviewViewModel
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

        public async Task<Rule11WorkspaceSaveResult> SaveWorkspaceAsync(Rule11ValidationRequest request, string reviewerEmail, string? reviewerName = null)
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
                        return new Rule11WorkspaceSaveResult
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
                    command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule11|{request.RunId.Value}|{request.ClientId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                    await command.ExecuteNonQueryAsync();

                    if (!string.IsNullOrWhiteSpace(reviewerEmail))
                        _pendingValidationCache.ClearPending(11, request.ClientId, reviewerEmail);

                    var currentWorkspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                    return new Rule11WorkspaceSaveResult
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

                var pending = _pendingValidationCache.GetPending<Rule11ValidationRequest, Rule11ValidationSummary>(11, request.ClientId, reviewerEmail);
                if (pending == null)
                {
                    return new Rule11WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run Rule 11 first so the current workspace is written to the system database."
                    };
                }

                if (!RequestsMatchForPendingSave(request, pending.Request))
                {
                    return new Rule11WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Workspace settings changed after validation. Run Rule 11 again before saving."
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
                _pendingValidationCache.ClearPending(11, request.ClientId, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule11WorkspaceSaveResult
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
                return new Rule11WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule11WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule11WorkspaceSaveResult
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
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule11|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                if (!string.IsNullOrWhiteSpace(reviewerEmail))
                    _pendingValidationCache.ClearPending(11, clientId.Value, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule11WorkspaceSaveResult
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
                return new Rule11WorkspaceSaveResult
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
                throw new InvalidOperationException("The selected Rule 11 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            var signoffRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(signoffRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can sign off a Rule 11 run.");

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
                throw new InvalidOperationException("The selected Rule 11 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0)
                return;
        }

        public Task<string> GenerateSqlAsync(Rule11ValidationRequest request)
        {
            ValidateRequest(request);

            var qualTable = Sanitise(request.QualTable);
            var pgTypes = BuildSqlStringList(NormalizePostgraduateTypes(request.PostgraduateTypesCsv));
            var excludeMClause = request.ExcludeMPrefixPattern ? "  AND Approved.QUAL__001 NOT LIKE 'M_____'\n" : string.Empty;

            var sql = $@"-- HEMIS RULE 11: QUALIFICATION SELECTION FROM dbo_QUAL
-- Rule mode: 100% population testing, no sampling
-- Control 1: approved undergraduate qualifications
-- Control 2: approved postgraduate qualifications

WITH ApprovedQualifications AS
(
    SELECT DISTINCT
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), QUAL.[_001])))) AS QUAL__001,
        LTRIM(RTRIM(CONVERT(nvarchar(255), QUAL.[_002]))) AS QUAL__002,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), QUAL.[_004])))) AS QUAL__004,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), QUAL.[_005])))) AS QUAL__005
    FROM [{qualTable}] QUAL
    WHERE QUAL.[_001] IS NOT NULL
      AND LTRIM(RTRIM(CONVERT(nvarchar(255), QUAL.[_001]))) <> ''
      AND UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), QUAL.[_004])))) = 'A'
),
Control1Results AS
(
    SELECT
        'Control_1' AS Control_Type,
        'CONTROL 1: Undergraduate approved qualifications' AS Control_Label,
        Approved.QUAL__001,
        Approved.QUAL__002,
        Approved.QUAL__004,
        Approved.QUAL__005
    FROM ApprovedQualifications Approved
    WHERE Approved.QUAL__005 NOT IN ({pgTypes})
{excludeMClause}),
Control2Results AS
(
    SELECT
        'Control_2' AS Control_Type,
        'CONTROL 2: Postgraduate approved qualifications' AS Control_Label,
        Approved.QUAL__001,
        Approved.QUAL__002,
        Approved.QUAL__004,
        Approved.QUAL__005
    FROM ApprovedQualifications Approved
    WHERE Approved.QUAL__005 IN ({pgTypes})
)
SELECT
    Control_Type,
    Control_Label,
    QUAL__001,
    QUAL__002,
    QUAL__004,
    QUAL__005,
    'PASS' AS Validation_Result
FROM Control1Results
UNION ALL
SELECT
    Control_Type,
    Control_Label,
    QUAL__001,
    QUAL__002,
    QUAL__004,
    QUAL__005,
    'PASS' AS Validation_Result
FROM Control2Results
ORDER BY Control_Type, QUAL__001;

SELECT
    (SELECT COUNT(*) FROM ApprovedQualifications) AS Approved_Qualifications,
    (SELECT COUNT(*) FROM Control1Results) AS Undergraduate_Qualifications,
    (SELECT COUNT(*) FROM Control2Results) AS Postgraduate_Qualifications;";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule11ValidationSummary> AnalyseAsync(Rule11ValidationRequest request, bool includeAllReviewRows)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var qualTable = Sanitise(request.QualTable);
            var pgTypes = BuildSqlStringList(NormalizePostgraduateTypes(request.PostgraduateTypesCsv));

            var qualCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{qualTable}];");

            await using var countCommand = conn.CreateConfiguredCommand();
            countCommand.CommandText = BuildPopulationCountSql(qualTable, pgTypes, request.ExcludeMPrefixPattern);
            await using var countReader = await countCommand.ExecuteReaderAsync();

            var approvedQualificationCount = 0;
            var undergraduateCount = 0;
            var postgraduateCount = 0;
            if (await countReader.ReadAsync())
            {
                approvedQualificationCount = GetInt(countReader, 0);
                undergraduateCount = GetInt(countReader, 1);
                postgraduateCount = GetInt(countReader, 2);
            }

            await countReader.CloseAsync();

            var reviewRows = await LoadControlRowsAsync(conn, qualTable, pgTypes, request.ExcludeMPrefixPattern, includeAllReviewRows ? null : BrowserPreviewRowLimit);
            reviewRows = NormalizeReviewRows(reviewRows);

            var controlSummaries = BuildControlSummaries(undergraduateCount, postgraduateCount, request.ExcludeMPrefixPattern);
            var totalValidated = controlSummaries.Sum(x => x.TotalCount);
            var passCount = controlSummaries.Sum(x => x.PassCount);
            var failCount = controlSummaries.Sum(x => x.FailCount);
            var isPreviewOnly = !includeAllReviewRows && totalValidated > reviewRows.Count;
            var overallPass = undergraduateCount > 0 && postgraduateCount > 0;

            return new Rule11ValidationSummary
            {
                Success = true,
                QualRecordCount = qualCount,
                ApprovedQualificationCount = approvedQualificationCount,
                UndergraduateCount = undergraduateCount,
                PostgraduateCount = postgraduateCount,
                TotalRequested = totalValidated,
                TotalValidated = totalValidated,
                DisplayedCount = reviewRows.Count,
                IsPreviewOnly = isPreviewOnly,
                PreviewLimit = isPreviewOnly ? BrowserPreviewRowLimit : 0,
                PassCount = passCount,
                FailCount = failCount,
                ExceptionRate = totalValidated == 0 ? 0m : Math.Round(failCount * 100m / totalValidated, 2),
                Status = overallPass ? "PASS" : "FAIL",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                QualTable = request.QualTable,
                ExcludeMPrefixPattern = request.ExcludeMPrefixPattern,
                PostgraduateTypesCsv = string.Join(",", NormalizePostgraduateTypes(request.PostgraduateTypesCsv)),
                TableLinkageText = $"{request.QualTable} filtered by dbo_QUAL._004, dbo_QUAL._005, and dbo_QUAL._001",
                RuleModeText = "100% population testing of approved undergraduate and postgraduate qualifications from dbo_QUAL",
                ProcedureSteps = BuildProcedureSteps(request.QualTable),
                ClientId = request.ClientId,
                ControlSummaries = controlSummaries,
                ReviewRows = reviewRows,
                Warning = includeAllReviewRows
                    ? "Rule 11 completed with the full dbo_QUAL qualification population result set."
                    : "Counts reflect the full dbo_QUAL qualification population result set. Browser review rows are limited for performance."
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule11ValidationRequest request, Rule11ValidationSummary summary, string? userEmail, string? userName, bool markWorkspaceSaved)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 11);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 11);
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
    @ClientID, @UserID, 11, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @BridgeTable, @CrseTable, NULL,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, @WorkspaceSavedAt, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "Qualification Selection from dbo_QUAL");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.QualTable);
            command.Parameters.AddWithValue("@BridgeTable", DBNull.Value);
            command.Parameters.AddWithValue("@CrseTable", DBNull.Value);
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
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule11|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            await UpdateStoredSummaryAsync(connection, runId, summary);

            return runId;
        }

        private static string BuildPopulationCountSql(string qualTable, string postgraduateTypesSql, bool excludeMPrefixPattern) => $@"
{BuildRule11SourceCtes(qualTable, postgraduateTypesSql, excludeMPrefixPattern)}
SELECT
    (SELECT COUNT(1) FROM ApprovedQualifications) AS ApprovedQualificationCount,
    (SELECT COUNT(1) FROM Control1Results) AS UndergraduateCount,
    (SELECT COUNT(1) FROM Control2Results) AS PostgraduateCount;";

        private async Task<List<Rule11ValidationRowRecord>> LoadControlRowsAsync(SqlConnection connection, string qualTable, string postgraduateTypesSql, bool excludeMPrefixPattern, int? maxRows)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = BuildAllControlsSql(qualTable, postgraduateTypesSql, excludeMPrefixPattern, maxRows);

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule11ValidationRowRecord>();
            while (await reader.ReadAsync())
            {
                var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    displayValues[reader.GetName(i)] = reader.IsDBNull(i)
                        ? null
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                }

                rows.Add(new Rule11ValidationRowRecord
                {
                    ValidationNumber = rows.Count + 1,
                    ControlType = ReadValue(displayValues, "Control_Type"),
                    ControlLabel = ReadValue(displayValues, "Control_Label"),
                    ValidationResult = ReadValue(displayValues, "Validation_Result"),
                    ValidationExplanation = ReadValue(displayValues, "Validation_Explanation"),
                    DisplayValues = displayValues
                });

                EnrichRule11DisplayValues(rows[^1]);
            }

            return rows;
        }

        private static string BuildAllControlsSql(string qualTable, string postgraduateTypesSql, bool excludeMPrefixPattern, int? maxRows)
        {
            var topClause = maxRows.HasValue && maxRows.Value > 0 ? $"TOP {maxRows.Value}" : string.Empty;

            return $@"
{BuildRule11SourceCtes(qualTable, postgraduateTypesSql, excludeMPrefixPattern)}
SELECT {topClause}
    1 AS Control_Sort,
    ResultRows.Control_Type,
    ResultRows.Control_Label,
    'PASS' AS Validation_Result,
    ResultRows.Validation_Explanation,
    ResultRows.QUAL__001,
    ResultRows.QUAL__002,
    ResultRows.QUAL__004,
    ResultRows.QUAL__005
FROM AllSelectedQualifications ResultRows
ORDER BY ResultRows.Control_Sort, ResultRows.QUAL__001;";
        }

        private static string BuildRule11SourceCtes(string qualTable, string postgraduateTypesSql, bool excludeMPrefixPattern)
        {
            var excludeMClause = excludeMPrefixPattern ? "      AND Approved.QUAL__001 NOT LIKE 'M_____'\n" : string.Empty;
            return $@"
WITH ApprovedQualifications AS
(
    SELECT DISTINCT
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), QUAL.[_001])))) AS QUAL__001,
        LTRIM(RTRIM(CONVERT(nvarchar(255), QUAL.[_002]))) AS QUAL__002,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), QUAL.[_004])))) AS QUAL__004,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), QUAL.[_005])))) AS QUAL__005
    FROM [{qualTable}] QUAL
    WHERE QUAL.[_001] IS NOT NULL
      AND LTRIM(RTRIM(CONVERT(nvarchar(255), QUAL.[_001]))) <> ''
      AND UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), QUAL.[_004])))) = 'A'
),
Control1Results AS
(
    SELECT
        QUAL__001,
        QUAL__002,
        QUAL__004,
        QUAL__005
    FROM ApprovedQualifications Approved
    WHERE Approved.QUAL__005 NOT IN ({postgraduateTypesSql})
{excludeMClause}),
Control2Results AS
(
    SELECT
        QUAL__001,
        QUAL__002,
        QUAL__004,
        QUAL__005
    FROM ApprovedQualifications Approved
    WHERE Approved.QUAL__005 IN ({postgraduateTypesSql})
),
AllSelectedQualifications AS
(
    SELECT
        1 AS Control_Sort,
        'Control_1' AS Control_Type,
        'CONTROL 1: Undergraduate approved qualifications' AS Control_Label,
        'Selected approved undergraduate qualification from dbo_QUAL.' AS Validation_Explanation,
        '" + (excludeMPrefixPattern ? "1" : "0") + @"' AS Exclude_M_Pattern,
        QUAL__001,
        QUAL__002,
        QUAL__004,
        QUAL__005
    FROM Control1Results
    UNION ALL
    SELECT
        2 AS Control_Sort,
        'Control_2' AS Control_Type,
        'CONTROL 2: Postgraduate approved qualifications' AS Control_Label,
        'Selected approved postgraduate qualification from dbo_QUAL.' AS Validation_Explanation,
        '" + (excludeMPrefixPattern ? "1" : "0") + @"' AS Exclude_M_Pattern,
        QUAL__001,
        QUAL__002,
        QUAL__004,
        QUAL__005
    FROM Control2Results
)";
        }

        private static List<Rule11ControlSummaryItemViewModel> BuildControlSummaries(
            int undergraduateCount,
            int postgraduateCount,
            bool excludeMPrefixPattern)
        {
            var control1Criteria = excludeMPrefixPattern
                ? "dbo_QUAL._004 = 'A' AND dbo_QUAL._005 NOT IN (07,27,28,49,72,73,08,30,50,74,75) AND NOT MATCH(dbo_QUAL._001, 'M_____')"
                : "dbo_QUAL._004 = 'A' AND dbo_QUAL._005 NOT IN (07,27,28,49,72,73,08,30,50,74,75)";

            return new List<Rule11ControlSummaryItemViewModel>
            {
                BuildControlSummary(
                    "Control_1",
                    "Control 1",
                    control1Criteria,
                    undergraduateCount),
                BuildControlSummary(
                    "Control_2",
                    "Control 2",
                    "dbo_QUAL._004 = 'A' AND dbo_QUAL._005 IN (07,27,28,49,72,73,08,30,50,74,75)",
                    postgraduateCount)
            };
        }

        private static Rule11ControlSummaryItemViewModel BuildControlSummary(
            string controlType,
            string controlLabel,
            string criteriaText,
            int totalCount)
        {
            var hasRows = totalCount > 0;
            return new Rule11ControlSummaryItemViewModel
            {
                ControlType = controlType,
                ControlLabel = controlLabel,
                CriteriaText = criteriaText,
                RequestedCount = totalCount,
                AvailableCount = totalCount,
                AchievedCount = totalCount,
                TotalCount = totalCount,
                PassCount = totalCount,
                FailCount = 0,
                Status = hasRows ? "PASS" : "FAIL"
            };
        }

        private static List<Rule11ValidationRowRecord> NormalizeReviewRows(IEnumerable<Rule11ValidationRowRecord>? rows)
        {
            var normalized = (rows ?? Enumerable.Empty<Rule11ValidationRowRecord>())
                .Select((row, index) =>
                {
                    row.ValidationNumber = index + 1;
                    return row;
                })
                .ToList();

            return normalized;
        }

        private async Task<Rule11ValidationSummary> ExpandSavedSummaryIfNeededAsync(Rule11ValidationSummary summary, string? server)
        {
            if (!summary.IsPreviewOnly &&
                summary.ReviewRows.Count >= summary.TotalValidated)
            {
                return summary;
            }

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(summary.Database) ||
                string.IsNullOrWhiteSpace(summary.QualTable))
            {
                return summary;
            }

            try
            {
                var expanded = await AnalyseAsync(
                    new Rule11ValidationRequest
                    {
                        ClientId = summary.ClientId,
                        RunId = summary.SavedRunId,
                        Server = server,
                        Database = summary.Database,
                        Driver = "ODBC Driver 17 for SQL Server",
                        QualTable = summary.QualTable,
                        ExcludeMPrefixPattern = summary.ExcludeMPrefixPattern,
                        PostgraduateTypesCsv = summary.PostgraduateTypesCsv
                    },
                    includeAllReviewRows: true);

                expanded.Timestamp = string.IsNullOrWhiteSpace(summary.Timestamp) ? expanded.Timestamp : summary.Timestamp;
                expanded.ClientId = summary.ClientId;
                expanded.SavedRunId = summary.SavedRunId;
                expanded.Warning = string.IsNullOrWhiteSpace(summary.Warning)
                    ? "Saved Rule 11 results were expanded from the stored browser preview to the full result set."
                    : $"{summary.Warning} Full saved results were reloaded from the saved Rule 11 configuration.";

                return expanded;
            }
            catch
            {
                return summary;
            }
        }

        private async Task<Rule11ValidationSummary> ExpandAndPersistSavedSummaryIfNeededAsync(SqlConnection connection, int runId, Rule11ValidationSummary summary, string? server)
        {
            var expanded = await ExpandSavedSummaryIfNeededAsync(summary, server);
            if (!ReferenceEquals(expanded, summary))
            {
                expanded.SavedRunId = runId;
                await UpdateStoredSummaryAsync(connection, runId, expanded);
            }

            return expanded;
        }

        private static Rule11ValidationSummary CloneSummary(Rule11ValidationSummary summary)
        {
            return new Rule11ValidationSummary
            {
                Success = summary.Success,
                QualRecordCount = summary.QualRecordCount,
                ApprovedQualificationCount = summary.ApprovedQualificationCount,
                UndergraduateCount = summary.UndergraduateCount,
                PostgraduateCount = summary.PostgraduateCount,
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
                QualTable = summary.QualTable,
                ExcludeMPrefixPattern = summary.ExcludeMPrefixPattern,
                PostgraduateTypesCsv = summary.PostgraduateTypesCsv,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText = summary.RuleModeText,
                ProcedureSteps = summary.ProcedureSteps.ToList(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries
                    .Select(item => new Rule11ControlSummaryItemViewModel
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

        private static Rule11ValidationRowRecord CloneReviewRow(Rule11ValidationRowRecord row)
        {
            return new Rule11ValidationRowRecord
            {
                ValidationNumber = row.ValidationNumber,
                ControlType = row.ControlType,
                ControlLabel = row.ControlLabel,
                ValidationResult = row.ValidationResult,
                ValidationExplanation = row.ValidationExplanation,
                DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static Rule11ValidationSummary CreateBrowserPreview(Rule11ValidationSummary summary)
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

            return new Rule11ValidationSummary
            {
                Success = summary.Success,
                QualRecordCount = summary.QualRecordCount,
                ApprovedQualificationCount = summary.ApprovedQualificationCount,
                UndergraduateCount = summary.UndergraduateCount,
                PostgraduateCount = summary.PostgraduateCount,
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
                QualTable = summary.QualTable,
                ExcludeMPrefixPattern = summary.ExcludeMPrefixPattern,
                PostgraduateTypesCsv = summary.PostgraduateTypesCsv,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText = summary.RuleModeText,
                ProcedureSteps = summary.ProcedureSteps.ToList(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries
                    .Select(item => new Rule11ControlSummaryItemViewModel
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

        private static List<Rule11ValidationRowRecord> TakePreviewRowsForControl(
            IReadOnlyList<Rule11ValidationRowRecord> rows,
            int limit)
        {
            if (limit <= 0 || rows.Count == 0)
                return new List<Rule11ValidationRowRecord>();

            var failRows = rows
                .Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var passRows = rows
                .Where(row => !string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var selected = new List<Rule11ValidationRowRecord>(limit);
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

        private static void ApplyBrowserPreview(Rule11ValidationSummary summary)
        {
            var preview = CreateBrowserPreview(summary);
            summary.DisplayedCount = preview.DisplayedCount;
            summary.IsPreviewOnly = preview.IsPreviewOnly;
            summary.PreviewLimit = preview.PreviewLimit;
            summary.ReviewRows = preview.ReviewRows;
        }

        private static Rule11ValidationRequest CloneValidationRequest(Rule11ValidationRequest request)
        {
            return new Rule11ValidationRequest
            {
                ClientId = request.ClientId,
                RunId = request.RunId,
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                QualTable = request.QualTable,
                ExcludeMPrefixPattern = request.ExcludeMPrefixPattern,
                PostgraduateTypesCsv = request.PostgraduateTypesCsv
            };
        }

        private static bool RequestsMatchForPendingSave(Rule11ValidationRequest current, Rule11ValidationRequest pending)
        {
            return current.ClientId == pending.ClientId &&
                   string.Equals(current.Server?.Trim(), pending.Server?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Database?.Trim(), pending.Database?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Driver?.Trim(), pending.Driver?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.QualTable?.Trim(), pending.QualTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   current.ExcludeMPrefixPattern == pending.ExcludeMPrefixPattern &&
                   string.Equals(
                       string.Join(",", NormalizePostgraduateTypes(current.PostgraduateTypesCsv)),
                       string.Join(",", NormalizePostgraduateTypes(pending.PostgraduateTypesCsv)),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static int GetControlSort(string? controlType) => controlType switch
        {
            "Control_1" => 1,
            "Control_2" => 2,
            _ => 99
        };

        private static List<string> BuildProcedureSteps(string qualTable) =>
            new()
            {
                $"From the {qualTable} table, select approved undergraduate qualifications where dbo_QUAL._004 = 'A'.",
                "Exclude postgraduate qualification types 07,27,28,49,72,73,08,30,50,74,75 from Control 1.",
                "Exclude qualification codes matching M_____ from Control 1.",
                $"From the same {qualTable} table, select approved postgraduate qualifications for Control 2.",
                "Return the full qualifying population for both controls. No sampling is applied."
            };

        private async Task EnsureColumnsExistAsync(string server, string database, string driver, string qualTable, string _, string __)
        {
            var qualColumns = await GetTableColumnsAsync(server, database, driver, qualTable);
            EnsureHasColumns(qualTable, qualColumns, "_001", "_002", "_004", "_005");
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

        private static void ValidateRequest(Rule11ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.QualTable))
                throw new InvalidOperationException("QUAL table is required.");

            ValidateObjectName(request.QualTable);
            _ = NormalizePostgraduateTypes(request.PostgraduateTypesCsv);
        }

        private static void ValidateRequest(Rule11VerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.QualTable))
                throw new InvalidOperationException("QUAL table is required.");

            ValidateObjectName(request.QualTable);
            _ = NormalizePostgraduateTypes(request.PostgraduateTypesCsv);
        }

        private static List<string> NormalizePostgraduateTypes(string? csv)
        {
            var values = (csv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => value.Trim().ToUpperInvariant())
                .Where(value => value.Length > 0)
                .ToList();

            if (values.Count == 0)
                throw new InvalidOperationException("Enter at least one postgraduate qualification type.");

            foreach (var value in values)
            {
                if (value.Any(ch => !char.IsLetterOrDigit(ch)))
                    throw new InvalidOperationException("Postgraduate qualification types may only contain letters and digits.");
            }

            return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string BuildSqlStringList(IEnumerable<string> values) =>
            string.Join(", ", values.Select(value => $"'{value.Replace("'", "''")}'"));

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

        private static Rule11ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                if (string.IsNullOrWhiteSpace(decoded))
                    return null;
                return JsonConvert.DeserializeObject<Rule11ValidationSummary>(decoded);
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

        private static async Task UpdateStoredSummaryAsync(SqlConnection connection, int runId, Rule11ValidationSummary summary)
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

        private static void EnrichRule11DisplayValues(Rule11ValidationRowRecord row)
        {
            var values = row.DisplayValues;
            var validationResult = ReadValue(values, "Validation_Result");
            var isPass = string.Equals(validationResult, "PASS", StringComparison.OrdinalIgnoreCase);
            var controlType = ReadValue(values, "Control_Type");
            var qual001 = FormatRule11ColumnValue(ReadValue(values, "QUAL__001"));
            var qual002 = FormatRule11ColumnValue(ReadValue(values, "QUAL__002"));
            var qual004 = FormatRule11ColumnValue(ReadValue(values, "QUAL__004"));
            var qual005 = FormatRule11ColumnValue(ReadValue(values, "QUAL__005"));
            var excludeM = string.Equals(ReadValue(values, "Exclude_M_Pattern"), "1", StringComparison.OrdinalIgnoreCase);

            var criteriaText = string.Equals(controlType, "Control_2", StringComparison.OrdinalIgnoreCase)
                ? "dbo_QUAL._004 = 'A' AND dbo_QUAL._005 IN (07,27,28,49,72,73,08,30,50,74,75)"
                : "dbo_QUAL._004 = 'A' AND dbo_QUAL._005 NOT IN (07,27,28,49,72,73,08,30,50,74,75) AND NOT MATCH(dbo_QUAL._001, 'M_____')";

            var validationExplanation = isPass
                ? $"Passed because qualification '{qual001}' ({qual002}) satisfies the selected Rule 11 criteria."
                : $"Failed because qualification '{qual001}' ({qual002}) does not satisfy the selected Rule 11 criteria.";
            var qualCriteriaMessage = $"dbo_QUAL._001 = '{qual001}', dbo_QUAL._004 = '{qual004}', dbo_QUAL._005 = '{qual005}'.";
            var detailMessage = string.Equals(controlType, "Control_2", StringComparison.OrdinalIgnoreCase)
                ? $"Qualification type '{qual005}' is treated as postgraduate."
                : excludeM
                    ? $"Qualification type '{qual005}' is treated as undergraduate and qualification code '{qual001}' does not match M_____."
                    : $"Qualification type '{qual005}' is treated as undergraduate. The M_____ exclusion was not applied for this run.";
            var finalResultMessage = isPass
                ? "Passed this criteria because the qualification belongs to the selected approved population."
                : "Failed this criteria because the qualification does not belong to the selected approved population.";

            values["FINAL_RULE_TEXT"] = criteriaText;
            values["Validation_Explanation"] = validationExplanation;
            values["QUAL_CRITERIA_MESSAGE"] = qualCriteriaMessage;
            values["QUAL_DETAIL_MESSAGE"] = detailMessage;
            values["FINAL_RESULT_MESSAGE"] = finalResultMessage;
            row.ValidationExplanation = validationExplanation;
        }

        private static string FormatRule11ColumnValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "[blank]" : value.Trim();

        private static int GetInt(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

        private static string ReadValue(IReadOnlyDictionary<string, string?> values, string key) =>
            values.TryGetValue(key, out var value) ? value ?? "" : "";

    }
}









