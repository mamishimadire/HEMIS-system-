using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.Helpers;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class Rule10Service : IRule10Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;
        private sealed record JoinDatasetTemplate(string DatasetCode, string DatasetLabel, string DefaultTableName, string[] KeyColumns, string[] CompositeKeyFields);

        private static readonly IReadOnlyList<JoinDatasetTemplate> Rule10JoinDatasets =
        [
            new("dbo_CREG", "dbo_CREG", "dbo_CREG", ["_030", "_001"], ["_001+_030"]),
            new("dbo_CRSE", "dbo_CRSE", "dbo_CRSE", ["_030"], []),
            new("dbo_STUD", "dbo_STUD", "dbo_STUD", ["_007", "_001"], ["_007+_001"]),
            new("dbo_QUAL", "dbo_QUAL", "dbo_QUAL", ["_001"], []),
            new("dbo_CESM", "dbo_CESM", "dbo_CESM", ["_001"], []),
            new("dbo_CRED", "dbo_CRED", "dbo_CRED", ["_030", "_001"], ["_001+_030"]),
            new("Cenus_dates", "Cenus_dates", "Cenus_dates", ["BLOCK_CODE"], []),
            new("Census_dates_design", "Census_dates_design", "Census_dates_design", ["BLOCK_CODE"], []),
            new("2024H16STUD", "2024H16STUD", "2024H16STUD", ["STUDNUM", "QUALCODE"], ["STUDNUM+QUALCODE"]),
            new("Prod_STUD", "Prod_STUD", "Prod_STUD", ["IAGSTNO", "IAGQUAL"], ["IAGSTNO+IAGQUAL"]),
            new("Prod_QUAL", "Prod_QUAL", "Prod_QUAL", ["IAIQUAL"], []),
            new("2024H16QUAL", "2024H16QUAL", "2024H16QUAL", ["QUALCODE"], []),
            new("Prod_CRSE", "Prod_CRSE", "Prod_CRSE", ["IALSUBJ"], []),
            new("2024H16CRSE", "2024H16CRSE", "2024H16CRSE", ["CRSECODE"], []),
            new("Employee_file", "Employee_file", "Employee_file", ["Personnel_Number"], []),
            new("dbo_PROF", "dbo_PROF", "dbo_PROF", ["_037"], []),
            new("Deceased_Students", "Deceased_Students", "Deceased_Students", ["STUDENT_NUMBER"], [])
        ];

        public Rule10Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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

        public async Task<Rule10TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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

                return new Rule10TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoQualTable = FindFirst(tables, ["dbo_QUAL", "QUAL"], ["qual"]),
                    AutoStudTable = FindFirst(tables, ["dbo_STUD", "STUD"], ["stud"]),
                    AutoCregTable = FindFirst(tables, ["dbo_CREG", "CREG"], ["creg"]),
                    AutoCrseTable = FindFirst(tables, ["dbo_CRSE", "CRSE"], ["crse"])
                };
            }
            catch (Exception ex)
            {
                return new Rule10TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule10ColumnSelectionResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                return new Rule10ColumnSelectionResult
                {
                    Success = true,
                    Columns = await GetTableColumnsAsync(server, database, driver, tableName)
                };
            }
            catch (Exception ex)
            {
                return new Rule10ColumnSelectionResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule10VerifyResult> VerifyTablesAsync(Rule10VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);

                if (request.RuleNumber == 10)
                {
                    var joinDatasets = await VerifyRule10JoinDatasetsAsync(
                        request.Server,
                        request.Database,
                        request.Driver,
                        request.Rule10JoinConfigJson,
                        throwOnMissingColumns: false);

                    return new Rule10VerifyResult
                    {
                        Success = true,
                        JoinDatasets = joinDatasets,
                        Error = joinDatasets.Any(item => item.MissingColumns.Count > 0)
                            ? "One or more documented key columns are missing from the selected tables."
                            : null
                    };
                }

                await EnsureColumnsExistAsync(request);

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                return new Rule10VerifyResult
                {
                    Success = true,
                    QualRecordCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(request.QualTable)}];"),
                    StudRecordCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(request.StudTable)}];"),
                    CregRecordCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(request.CregTable)}];"),
                    CrseRecordCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(request.CrseTable)}];")
                };
            }
            catch (Exception ex)
            {
                return new Rule10VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule10ValidationSummary> RunValidationAsync(Rule10ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                var rule = IntegrityRuleCatalog.Get(request.RuleNumber);
                await EnsureColumnsExistAsync(request);

                var browserSummary = await AnalyseAsync(request, includeAllReviewRows: false);
                if (browserSummary.Success && request.ClientId > 0)
                {
                    try
                    {
                        browserSummary.SavedRunId = await SaveValidationRunAsync(
                            CloneValidationRequest(request),
                            CloneSummary(browserSummary),
                            userEmail,
                            userName,
                            markWorkspaceSaved: false);

                        if (!string.IsNullOrWhiteSpace(userEmail))
                            _pendingValidationCache.ClearPending(request.RuleNumber, request.ClientId, userEmail!);
                    }
                    catch (Exception ex)
                    {
                        browserSummary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                if (!browserSummary.SavedRunId.HasValue)
                {
                    if (browserSummary.Success && request.ClientId > 0 && !string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.StorePending(request.RuleNumber, request.ClientId, userEmail!, request, CloneSummary(browserSummary), userName);

                    browserSummary.Warning = string.IsNullOrWhiteSpace(browserSummary.Warning)
                        ? $"Counts reflect the full {rule.RuleLabel} integrity result set. Browser exception rows are limited for performance."
                        : browserSummary.Warning;
                }
                else
                {
                    browserSummary.Warning = $"The current {rule.RuleLabel} integrity run has been written to the system database. Click Save Workspace to finalize it for signoff.";
                }

                ApplyBrowserPreview(browserSummary);
                return browserSummary;
            }
            catch (Exception ex)
            {
                return new Rule10ValidationSummary
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule10ValidationSummary> GetExportSummaryAsync(Rule10ValidationRequest request)
        {
            ValidateRequest(request);
            await EnsureColumnsExistAsync(request);
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public Task<Rule10ValidationSummary?> GetPendingValidationPreviewAsync(int ruleNumber, int clientId, string reviewerEmail)
        {
            var pending = _pendingValidationCache.GetPending<Rule10ValidationRequest, Rule10ValidationSummary>(ruleNumber, clientId, reviewerEmail);
            if (pending == null)
                return Task.FromResult<Rule10ValidationSummary?>(null);

            var preview = CloneSummary(pending.Summary);
            preview.SavedRunId = null;
            preview.Warning = $"This {IntegrityRuleCatalog.Get(ruleNumber).RuleLabel} integrity validation is still pending. Click Save Workspace to write it to the system database.";
            ApplyBrowserPreview(preview);
            return Task.FromResult<Rule10ValidationSummary?>(preview);
        }

        public Task<bool> HasPendingValidationAsync(int ruleNumber, int clientId, string reviewerEmail)
            => Task.FromResult(_pendingValidationCache.HasPending(ruleNumber, clientId, reviewerEmail));

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule10WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int ruleNumber, int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
    ISNULL(vr.DeceasedTable, '') AS QualTable,
    ISNULL(vr.StudTable, '') AS StudTable,
    ISNULL(vr.BridgeTable, '') AS CregTable,
    ISNULL(vr.CrseTable, '') AS CrseTable,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = @RuleNumber
  AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var runId = reader.GetInt32(0);
            var workspaceClientId = reader.GetInt32(1);
            var server = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var database = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var qualTable = reader.IsDBNull(4) ? "dbo_QUAL" : reader.GetString(4);
            var studTable = reader.IsDBNull(5) ? "dbo_STUD" : reader.GetString(5);
            var cregTable = reader.IsDBNull(6) ? "dbo_CREG" : reader.GetString(6);
            var crseTable = reader.IsDBNull(7) ? "dbo_CRSE" : reader.GetString(7);
            var currentStatus = reader.IsDBNull(8) ? "" : reader.GetString(8);
            var lastEditedByUserName = reader.IsDBNull(9) ? null : reader.GetString(9);
            DateTime? lastEditedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10);
            var encodedSummary = reader.IsDBNull(11) ? null : reader.GetString(11);

            await reader.CloseAsync();

            var deserializedSummary = DeserializeSummary(encodedSummary);
            if (deserializedSummary != null && includeSummary)
            {
                ApplyBrowserPreview(deserializedSummary);
            }

            var summary = includeSummary ? deserializedSummary : null;

            var workspace = new Rule10WorkspaceStateViewModel
            {
                RuleNumber = ruleNumber,
                ClientId = workspaceClientId,
                RunId = runId,
                Server = server,
                Database = database,
                Driver = "ODBC Driver 17 for SQL Server",
                QualTable = deserializedSummary?.QualTable ?? qualTable,
                StudTable = deserializedSummary?.StudTable ?? studTable,
                CregTable = deserializedSummary?.CregTable ?? cregTable,
                CrseTable = deserializedSummary?.CrseTable ?? crseTable,
                QualColumn = deserializedSummary?.QualColumn ?? GetDefaultQualColumn(ruleNumber),
                StudColumn = deserializedSummary?.StudColumn ?? GetDefaultStudColumn(ruleNumber),
                CregColumn = deserializedSummary?.CregColumn ?? GetDefaultCregColumn(ruleNumber),
                CrseColumn = deserializedSummary?.CrseColumn ?? GetDefaultCrseColumn(ruleNumber),
                RuleParameterJson = deserializedSummary?.RuleParameterJson ?? GetDefaultRuleParameterJson(ruleNumber),
                Rule10JoinConfigJson = deserializedSummary?.Rule10JoinConfigJson ?? "",
                CurrentStatus = currentStatus,
                LastEditedByUserName = lastEditedByUserName,
                LastEditedAt = lastEditedAt,
                Summary = summary
            };

            if (summary != null)
                workspace.CurrentStatus = summary.Status;

            workspace.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId!.Value, currentUserId);
            workspace.HasDataAnalystSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            var currentRoleSignoff = signoffs.FirstOrDefault(s =>
                HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff = currentRoleSignoff != null;
            workspace.CurrentUserSignoffComment = currentRoleSignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved = await IsWorkspaceSavedAsync(connection, workspace.RunId.Value);

            if (workspace.Summary != null)
                workspace.Summary.SavedRunId = workspace.RunId;

            return workspace;
        }

        public async Task<Rule10RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.RuleNumber, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber BETWEEN 1 AND 10;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var savedRunId = reader.GetInt32(0);
            var clientId = reader.GetInt32(1);
            var ruleNumber = reader.GetInt32(2);
            var isCurrentRun = !reader.IsDBNull(3) && reader.GetBoolean(3);
            var engagementName = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var maconomyNumber = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var sourceServer = reader.IsDBNull(6) ? "" : reader.GetString(6);
            var encodedSummary = reader.IsDBNull(7) ? null : reader.GetString(7);
            var summary = DeserializeSummary(encodedSummary);
            if (summary == null)
                return null;

            var rule = IntegrityRuleCatalog.Get(ruleNumber);
            summary.RuleNumber = ruleNumber;
            summary.RuleLabel = rule.RuleLabel;
            summary.RuleTitle = rule.RuleTitle;
            summary.ClientId = clientId;
            if (summary.SavedRunId.GetValueOrDefault() <= 0)
                summary.SavedRunId = runId;

            await reader.CloseAsync();

            if (includeFullResults)
            {
                summary.DisplayedCount = summary.ReviewRows.Count;
            }
            else
            {
                ApplyBrowserPreview(summary);
            }

            var review = new Rule10RunReviewViewModel
            {
                RunId = savedRunId,
                RuleNumber = ruleNumber,
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

        public async Task<Rule10WorkspaceSaveResult> SaveWorkspaceAsync(Rule10ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                ValidateRequest(request);
                var rule = IntegrityRuleCatalog.Get(request.RuleNumber);

                if (request.RunId.HasValue && request.RunId.Value > 0)
                {
                    await using var connection = await OpenSystemConnectionAsync();
                    var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                    if (!clientId.HasValue || clientId.Value != request.ClientId)
                    {
                        return new Rule10WorkspaceSaveResult
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
                    command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule{request.RuleNumber}|{request.RunId.Value}|{request.ClientId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                    await command.ExecuteNonQueryAsync();

                    if (!string.IsNullOrWhiteSpace(reviewerEmail))
                        _pendingValidationCache.ClearPending(request.RuleNumber, request.ClientId, reviewerEmail);

                    var currentWorkspace = await GetCurrentWorkspaceStateAsync(request.RuleNumber, request.ClientId, reviewerEmail, includeSummary: false);
                    return new Rule10WorkspaceSaveResult
                    {
                        Success = true,
                        Message = clearedSignoffs > 0
                            ? "Workspace saved. Existing signoffs were removed and the run must be reviewed again."
                            : "Workspace saved.",
                        SignoffsCleared = clearedSignoffs > 0,
                        ClearedSignoffCount = clearedSignoffs,
                        Workspace = currentWorkspace
                    };
                }

                var pending = _pendingValidationCache.GetPending<Rule10ValidationRequest, Rule10ValidationSummary>(request.RuleNumber, request.ClientId, reviewerEmail);
                if (pending == null)
                {
                    return new Rule10WorkspaceSaveResult
                    {
                        Success = false,
                        Error = $"Run {rule.RuleLabel} first so the current workspace is written to the system database."
                    };
                }

                if (!RequestsMatchForPendingSave(request, pending.Request))
                {
                    return new Rule10WorkspaceSaveResult
                    {
                        Success = false,
                        Error = $"Workspace settings changed after validation. Run {rule.RuleLabel} again before saving."
                    };
                }

                var runId = await SaveValidationRunAsync(
                    CloneValidationRequest(pending.Request),
                    CloneSummary(pending.Summary),
                    reviewerEmail,
                    reviewerName ?? pending.ReviewerName,
                    markWorkspaceSaved: true);

                _pendingValidationCache.ClearPending(request.RuleNumber, request.ClientId, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(request.RuleNumber, request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule10WorkspaceSaveResult
                {
                    Success = true,
                    Message = $"Workspace saved. Current run: #{runId}",
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule10WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule10WorkspaceSaveResult> BeginWorkspaceEditAsync(int ruleNumber, int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule10WorkspaceSaveResult
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
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule{ruleNumber}|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                if (!string.IsNullOrWhiteSpace(reviewerEmail))
                    _pendingValidationCache.ClearPending(ruleNumber, clientId.Value, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(ruleNumber, clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule10WorkspaceSaveResult
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
                return new Rule10WorkspaceSaveResult
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
                throw new InvalidOperationException("The selected Rules 1-10 integrity run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            var signoffRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(signoffRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can sign off a Rules 1-10 integrity run.");

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
                throw new InvalidOperationException("The selected Rules 1-10 integrity run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0)
                return;
        }

        public Task<string> GenerateSqlAsync(Rule10ValidationRequest request)
        {
            ValidateRequest(request);
            var rule = IntegrityRuleCatalog.Get(request.RuleNumber);

            if (request.RuleNumber == 10)
                return Task.FromResult(BuildRule10JoinVerificationSql(request, rule));

            var definitions = BuildIntegrityRuleDefinitions(request);

            var sql = $@"-- HEMIS {rule.RuleLabel.ToUpperInvariant()}: {rule.RuleTitle.ToUpperInvariant()}
-- Rule mode: 100% integrity verification, no sampling
-- Database: {request.Database}
-- Tables: {request.QualTable}, {request.StudTable}, {request.CregTable}, {request.CrseTable}

SELECT 'dbo_QUAL' AS TableName, COUNT(*) AS RecordCount FROM [{Sanitise(request.QualTable)}]
UNION ALL
SELECT 'dbo_STUD' AS TableName, COUNT(*) AS RecordCount FROM [{Sanitise(request.StudTable)}]
UNION ALL
SELECT 'dbo_CREG' AS TableName, COUNT(*) AS RecordCount FROM [{Sanitise(request.CregTable)}]
UNION ALL
SELECT 'dbo_CRSE' AS TableName, COUNT(*) AS RecordCount FROM [{Sanitise(request.CrseTable)}];

";

            foreach (var definition in definitions)
            {
                sql += $@"
-- ============================================================
-- RULE {definition.RuleId}: {definition.Label}
-- {definition.CriteriaText}
-- Table Scope: {definition.TableName}
-- Severity: {definition.Severity}
-- ============================================================
{definition.CountSql}

{definition.ReviewSql}
";
            }

            return Task.FromResult(sql.Trim());
        }

        private static Rule10ValidationSummary CreateBrowserPreview(Rule10ValidationSummary summary)
        {
            var failRows = summary.ReviewRows
                .Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .OrderBy(row => row.RuleId)
                .ThenBy(row => row.ValidationNumber)
                .Select(CloneReviewRow)
                .ToList();

            var passRows = summary.ReviewRows
                .Where(row => string.Equals(row.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase))
                .OrderBy(row => row.RuleId)
                .ThenBy(row => row.ValidationNumber)
                .Select(CloneReviewRow)
                .ToList();

            var failTake = Math.Min(failRows.Count, Math.Max(BrowserPreviewRowLimit / 2, 1));
            var passTake = Math.Min(passRows.Count, Math.Max(BrowserPreviewRowLimit - failTake, 0));

            if (failTake == 0)
                passTake = Math.Min(passRows.Count, BrowserPreviewRowLimit);
            else if (passTake == 0)
                failTake = Math.Min(failRows.Count, BrowserPreviewRowLimit);

            var previewRows = failRows.Take(failTake)
                .Concat(passRows.Take(passTake))
                .Take(BrowserPreviewRowLimit)
                .Select((row, index) =>
                {
                    row.ValidationNumber = index + 1;
                    return row;
                })
                .ToList();

            var clone = CloneSummary(summary);
            clone.DisplayedCount = previewRows.Count;
            clone.IsPreviewOnly = summary.TotalValidated > previewRows.Count;
            clone.PreviewLimit = previewRows.Count;
            clone.ReviewRows = previewRows;
            return clone;
        }

        private static void ApplyBrowserPreview(Rule10ValidationSummary summary)
        {
            var preview = CreateBrowserPreview(summary);
            summary.DisplayedCount = preview.DisplayedCount;
            summary.IsPreviewOnly = preview.IsPreviewOnly;
            summary.PreviewLimit = preview.PreviewLimit;
            summary.ReviewRows = preview.ReviewRows;
        }

        private static Rule10ValidationSummary CloneSummary(Rule10ValidationSummary summary)
        {
            return new Rule10ValidationSummary
            {
                Success = summary.Success,
                RuleNumber = summary.RuleNumber,
                RuleLabel = summary.RuleLabel,
                RuleTitle = summary.RuleTitle,
                QualRecordCount = summary.QualRecordCount,
                StudRecordCount = summary.StudRecordCount,
                CregRecordCount = summary.CregRecordCount,
                CrseRecordCount = summary.CrseRecordCount,
                TotalChecks = summary.TotalChecks,
                PassedChecks = summary.PassedChecks,
                FailedChecks = summary.FailedChecks,
                TotalIssues = summary.TotalIssues,
                HighSeverityCount = summary.HighSeverityCount,
                TotalRequested = summary.TotalRequested,
                TotalValidated = summary.TotalValidated,
                DisplayedCount = summary.DisplayedCount,
                IsPreviewOnly = summary.IsPreviewOnly,
                PreviewLimit = summary.PreviewLimit,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                OverallStatusText = summary.OverallStatusText,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                QualTable = summary.QualTable,
                StudTable = summary.StudTable,
                CregTable = summary.CregTable,
                CrseTable = summary.CrseTable,
                QualColumn = summary.QualColumn,
                StudColumn = summary.StudColumn,
                CregColumn = summary.CregColumn,
                CrseColumn = summary.CrseColumn,
                RuleParameterJson = summary.RuleParameterJson,
                Rule10JoinConfigJson = summary.Rule10JoinConfigJson,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText = summary.RuleModeText,
                ProcedureSteps = summary.ProcedureSteps.ToList(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries.Select(item => new Rule10ControlSummaryItemViewModel
                {
                    RuleId = item.RuleId,
                    ControlType = item.ControlType,
                    ControlLabel = item.ControlLabel,
                    CriteriaText = item.CriteriaText,
                    TableName = item.TableName,
                    Severity = item.Severity,
                    ErrorCount = item.ErrorCount,
                    RequestedCount = item.RequestedCount,
                    AvailableCount = item.AvailableCount,
                    AchievedCount = item.AchievedCount,
                    TotalCount = item.TotalCount,
                    PassCount = item.PassCount,
                    FailCount = item.FailCount,
                    Status = item.Status
                }).ToList(),
                ReviewRows = summary.ReviewRows.Select(CloneReviewRow).ToList(),
                Warning = summary.Warning,
                Error = summary.Error
            };
        }

        private static Rule10ValidationRowRecord CloneReviewRow(Rule10ValidationRowRecord row)
        {
            return new Rule10ValidationRowRecord
            {
                ValidationNumber = row.ValidationNumber,
                RuleId = row.RuleId,
                ControlType = row.ControlType,
                ControlLabel = row.ControlLabel,
                ValidationResult = row.ValidationResult,
                ValidationExplanation = row.ValidationExplanation,
                DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static Rule10ValidationRequest CloneValidationRequest(Rule10ValidationRequest request)
        {
            return new Rule10ValidationRequest
            {
                ClientId = request.ClientId,
                RunId = request.RunId,
                RuleNumber = request.RuleNumber,
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                QualTable = request.QualTable,
                StudTable = request.StudTable,
                CregTable = request.CregTable,
                CrseTable = request.CrseTable,
                QualColumn = request.QualColumn,
                StudColumn = request.StudColumn,
                CregColumn = request.CregColumn,
                CrseColumn = request.CrseColumn,
                RuleParameterJson = request.RuleParameterJson,
                Rule10JoinConfigJson = request.Rule10JoinConfigJson
            };
        }

        private static bool RequestsMatchForPendingSave(Rule10ValidationRequest current, Rule10ValidationRequest pending)
        {
            return current.ClientId == pending.ClientId &&
                   current.RuleNumber == pending.RuleNumber &&
                   string.Equals(current.Server?.Trim(), pending.Server?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Database?.Trim(), pending.Database?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Driver?.Trim(), pending.Driver?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.QualTable?.Trim(), pending.QualTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.StudTable?.Trim(), pending.StudTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.CregTable?.Trim(), pending.CregTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.CrseTable?.Trim(), pending.CrseTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.QualColumn?.Trim(), pending.QualColumn?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.StudColumn?.Trim(), pending.StudColumn?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.CregColumn?.Trim(), pending.CregColumn?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.CrseColumn?.Trim(), pending.CrseColumn?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.RuleParameterJson?.Trim(), pending.RuleParameterJson?.Trim(), StringComparison.Ordinal) &&
                   string.Equals(current.Rule10JoinConfigJson?.Trim(), pending.Rule10JoinConfigJson?.Trim(), StringComparison.Ordinal);
        }

        private static List<string> BuildProcedureSteps(Rule10ValidationRequest request)
        {
            var rule = IntegrityRuleCatalog.Get(request.RuleNumber);
            if (request.RuleNumber == 10)
            {
                return
                [
                    "Review the configured join datasets for this rule.",
                    "Confirm that each selected table exists in the chosen database.",
                    "Confirm that every documented key column exists on the selected table.",
                    "Flag any missing tables or missing key columns as failing exceptions.",
                    $"{rule.RuleLabel} passes only when every documented dataset has all required key columns."
                ];
            }

            return new List<string>
            {
                $"Verify {rule.RuleLabel} using the required HEMIS tables for this control.",
                rule.DetailedDescription,
                $"Apply the audit criteria: {rule.CriteriaText}.",
                "Capture every failing row for review and export.",
                $"{rule.RuleLabel} passes only when the check returns zero issues."
            };
        }

        private async Task<Rule10ValidationSummary> AnalyseAsync(Rule10ValidationRequest request, bool includeAllReviewRows)
        {
            var rule = IntegrityRuleCatalog.Get(request.RuleNumber);

            if (request.RuleNumber == 10)
                return await AnalyseRule10JoinVerificationAsync(request);

            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var definitions = BuildIntegrityRuleDefinitions(request);
            var reviewRows = new List<Rule10ValidationRowRecord>();
            var summaries = new List<Rule10ControlSummaryItemViewModel>();
            var detailLimit = includeAllReviewRows ? (int?)null : BrowserPreviewRowLimit;
            var totalValidatedRows = 0;
            var totalFailedRows = 0;

            foreach (var definition in definitions)
            {
                if (!string.IsNullOrWhiteSpace(definition.PrepSql))
                {
                    await ExecuteNonQueryAsync(conn, definition.PrepSql);
                }

                var errorCount = await CountAsync(conn, definition.CountSql);
                var validatedCount = await CountAsync(conn, definition.TotalSql);
                var passCount = Math.Max(validatedCount - errorCount, 0);
                summaries.Add(new Rule10ControlSummaryItemViewModel
                {
                    RuleId = definition.RuleId,
                    ControlType = $"Rule_{definition.RuleId}",
                    ControlLabel = $"Rule {definition.RuleId}",
                    CriteriaText = definition.CriteriaText,
                    TableName = definition.TableName,
                    Severity = definition.Severity,
                    ErrorCount = errorCount,
                    RequestedCount = validatedCount,
                    AvailableCount = validatedCount,
                    AchievedCount = passCount,
                    TotalCount = validatedCount,
                    PassCount = passCount,
                    FailCount = errorCount,
                    Status = errorCount == 0 ? "PASS" : "FAIL"
                });

                totalValidatedRows += validatedCount;
                totalFailedRows += errorCount;
                reviewRows.AddRange(await LoadValidationRowsAsync(conn, definition, detailLimit));
            }

            reviewRows = reviewRows
                .OrderBy(row => row.RuleId)
                .ThenBy(row => row.ValidationNumber)
                .Select((row, index) =>
                {
                    row.ValidationNumber = index + 1;
                    return row;
                })
                .ToList();

            var totalChecks = summaries.Count;
            var passedChecks = summaries.Count(item => string.Equals(item.Status, "PASS", StringComparison.OrdinalIgnoreCase));
            var failedChecks = totalChecks - passedChecks;
            var totalIssues = summaries.Sum(item => item.ErrorCount);
            var highSeverityCount = summaries.Count(item => item.ErrorCount > 0 && string.Equals(item.Severity, "High", StringComparison.OrdinalIgnoreCase));
            var isPreviewOnly = !includeAllReviewRows && totalValidatedRows > reviewRows.Count;

            return new Rule10ValidationSummary
            {
                Success = true,
                RuleNumber = rule.RuleNumber,
                RuleLabel = rule.RuleLabel,
                RuleTitle = rule.RuleTitle,
                QualRecordCount = RequiresTable(request.RuleNumber, "QUAL") ? await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(request.QualTable)}];") : 0,
                StudRecordCount = RequiresTable(request.RuleNumber, "STUD") ? await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(request.StudTable)}];") : 0,
                CregRecordCount = RequiresTable(request.RuleNumber, "CREG") ? await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(request.CregTable)}];") : 0,
                CrseRecordCount = RequiresTable(request.RuleNumber, "CRSE") ? await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(request.CrseTable)}];") : 0,
                TotalChecks = totalChecks,
                PassedChecks = passedChecks,
                FailedChecks = failedChecks,
                TotalIssues = totalIssues,
                HighSeverityCount = highSeverityCount,
                TotalRequested = totalChecks,
                TotalValidated = totalValidatedRows,
                DisplayedCount = reviewRows.Count,
                IsPreviewOnly = isPreviewOnly,
                PreviewLimit = isPreviewOnly ? BrowserPreviewRowLimit : 0,
                PassCount = Math.Max(totalValidatedRows - totalFailedRows, 0),
                FailCount = totalFailedRows,
                ExceptionRate = totalValidatedRows == 0 ? 0m : Math.Round(totalFailedRows * 100m / totalValidatedRows, 2),
                Status = failedChecks == 0 ? "PASS" : "FAIL",
                OverallStatusText = failedChecks == 0 ? "EXCELLENT" : passedChecks >= 7 ? "ATTENTION REQUIRED" : "CRITICAL",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                QualTable = request.QualTable,
                StudTable = request.StudTable,
                CregTable = request.CregTable,
                CrseTable = request.CrseTable,
                QualColumn = request.QualColumn,
                StudColumn = request.StudColumn,
                CregColumn = request.CregColumn,
                CrseColumn = request.CrseColumn,
                RuleParameterJson = request.RuleParameterJson,
                Rule10JoinConfigJson = request.Rule10JoinConfigJson,
                TableLinkageText = $"{request.StudTable} -> {request.QualTable}, {request.CregTable} -> {request.CrseTable}, {request.CregTable} -> {request.StudTable}",
                RuleModeText = $"{rule.RuleLabel} integrity verification",
                ProcedureSteps = BuildProcedureSteps(request),
                ClientId = request.ClientId,
                ControlSummaries = summaries,
                ReviewRows = reviewRows,
                Warning = includeAllReviewRows
                    ? $"{rule.RuleLabel} completed with the full saved result set."
                    : $"Counts reflect the full {rule.RuleLabel} result set. Browser rows are limited to a 10-row pass/fail sample for performance."
            };
        }

        private async Task PrepareAnalysisTablesAsync(SqlConnection connection, Rule10ValidationRequest request)
        {
            var qual = Sanitise(request.QualTable);
            var stud = Sanitise(request.StudTable);
            var creg = Sanitise(request.CregTable);
            var crse = Sanitise(request.CrseTable);

            var sql = $@"
DROP TABLE IF EXISTS #Rule10Qual;
DROP TABLE IF EXISTS #Rule10Stud;
DROP TABLE IF EXISTS #Rule10Creg;
DROP TABLE IF EXISTS #Rule10Crse;

SELECT
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_001]))) AS QUAL__001,
    LTRIM(RTRIM(CONVERT(nvarchar(4000), [_002]))) AS QUAL__002,
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_003]))) AS QUAL__003,
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_004]))) AS QUAL__004,
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_005]))) AS QUAL__005
INTO #Rule10Qual
FROM [{qual}];

CREATE NONCLUSTERED INDEX IX_Rule10Qual_001 ON #Rule10Qual (QUAL__001);
CREATE NONCLUSTERED INDEX IX_Rule10Qual_004 ON #Rule10Qual (QUAL__004);
CREATE NONCLUSTERED INDEX IX_Rule10Qual_005 ON #Rule10Qual (QUAL__005);

SELECT
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_001]))) AS STUD__001,
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_007]))) AS STUD__007,
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_019]))) AS STUD__019,
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_024]))) AS STUD__024,
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_025]))) AS STUD__025,
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_106]))) AS STUD__106
INTO #Rule10Stud
FROM [{stud}];

CREATE NONCLUSTERED INDEX IX_Rule10Stud_001 ON #Rule10Stud (STUD__001);
CREATE NONCLUSTERED INDEX IX_Rule10Stud_007 ON #Rule10Stud (STUD__007);
CREATE NONCLUSTERED INDEX IX_Rule10Stud_019 ON #Rule10Stud (STUD__019);
CREATE NONCLUSTERED INDEX IX_Rule10Stud_106 ON #Rule10Stud (STUD__106);

SELECT
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_001]))) AS CREG__001,
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_007]))) AS CREG__007,
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_030]))) AS CREG__030,
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_032]))) AS CREG__032
INTO #Rule10Creg
FROM [{creg}];

CREATE NONCLUSTERED INDEX IX_Rule10Creg_007 ON #Rule10Creg (CREG__007);
CREATE NONCLUSTERED INDEX IX_Rule10Creg_030 ON #Rule10Creg (CREG__030);

SELECT
    LTRIM(RTRIM(CONVERT(nvarchar(255), [_030]))) AS CRSE__030
INTO #Rule10Crse
FROM [{crse}];

CREATE NONCLUSTERED INDEX IX_Rule10Crse_030 ON #Rule10Crse (CRSE__030);";

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        private async Task<int> SaveValidationRunAsync(Rule10ValidationRequest request, Rule10ValidationSummary summary, string? userEmail, string? userName, bool markWorkspaceSaved)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            var rule = IntegrityRuleCatalog.Get(request.RuleNumber);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, request.RuleNumber);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, request.RuleNumber);
            var validationRows = summary.ReviewRows.ToList();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
INSERT INTO dbo.ValidationRuns
(
    ClientID, UserID, RuleNumber, RuleName, Status, TotalRecords, PassCount, FailCount, ExceptionRate, RunTimestamp,
    HemisServer, AuditDatabase, StudTable, DeceasedTable, BridgeTable, CrseTable,
    ExceptionsJSON, ResultsJSON, RunByUserName, LastEditedByUserName, LastEditedAt, PreviousHash, RecordHash, WorkspaceSavedAt, IsCurrent
)
VALUES
(
    @ClientID, @UserID, @RuleNumber, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @DeceasedTable, @BridgeTable, @CrseTable,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, @WorkspaceSavedAt, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleNumber", request.RuleNumber);
            command.Parameters.AddWithValue("@RuleName", $"{rule.RuleLabel} Integrity Check");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalChecks);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.StudTable);
            command.Parameters.AddWithValue("@DeceasedTable", request.QualTable);
            command.Parameters.AddWithValue("@BridgeTable", request.CregTable);
            command.Parameters.AddWithValue("@CrseTable", request.CrseTable);
            var persistedSummary = CloneSummary(summary);
            persistedSummary.SavedRunId = summary.SavedRunId;
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(validationRows)));
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
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule{request.RuleNumber}|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalChecks}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            await UpdateStoredSummaryAsync(connection, runId, summary);
            return runId;
        }

        private static List<IntegrityRuleDefinition> BuildIntegrityRuleDefinitions(Rule10ValidationRequest request)
        {
            var qual = Sanitise(request.QualTable);
            var stud = Sanitise(request.StudTable);
            var creg = Sanitise(request.CregTable);
            var crse = Sanitise(request.CrseTable);
            var qualColumn = Sanitise((request.RuleNumber == 1 || request.RuleNumber == 2 || request.RuleNumber == 3 || request.RuleNumber == 7)
                ? ResolveSelectedColumn(request.QualColumn)
                : GetDefaultQualColumn(request.RuleNumber));
            var studColumn = Sanitise((request.RuleNumber == 5 || request.RuleNumber == 6 || request.RuleNumber == 7 || request.RuleNumber == 9)
                ? ResolveSelectedColumn(request.StudColumn)
                : GetDefaultStudColumn(request.RuleNumber));
            var cregColumn = Sanitise((request.RuleNumber == 8 || request.RuleNumber == 9)
                ? ResolveSelectedColumn(request.CregColumn)
                : GetDefaultCregColumn(request.RuleNumber));
            var crseColumn = Sanitise((request.RuleNumber == 4 || request.RuleNumber == 8)
                ? ResolveSelectedColumn(request.CrseColumn)
                : GetDefaultCrseColumn(request.RuleNumber));
            var parameters = ParseRuleParameters(request.RuleParameterJson, request.RuleNumber);
            var rule5PlaceholderValue = ResolveRuleParameterValue(parameters.MatchValue, "9999999");
            var rule5PlaceholderValueSql = ToSqlLiteral(rule5PlaceholderValue);
            var rule7ContextColumn = ResolveSelectedColumn(parameters.ContextColumn);
            var rule7ContextColumnSafe = Sanitise(rule7ContextColumn);
            var rule8ContextColumn = ResolveSelectedColumn(parameters.ContextColumn);
            var rule8ContextColumnSafe = Sanitise(rule8ContextColumn);
            var rule9CregContextColumn = ResolveSelectedColumn(parameters.ContextColumn);
            var rule9CregContextColumnSafe = Sanitise(rule9CregContextColumn);
            var rule9StudContextColumn = ResolveSelectedColumn(parameters.SecondaryContextColumn);
            var rule9StudContextColumnSafe = Sanitise(rule9StudContextColumn);

            var qualValue = $"LTRIM(RTRIM(CONVERT(nvarchar(255), q.[{qualColumn}])))";
            var studValue = $"LTRIM(RTRIM(CONVERT(nvarchar(255), s.[{studColumn}])))";
            var cregValue = $"LTRIM(RTRIM(CONVERT(nvarchar(255), cr.[{cregColumn}])))";
            var crseValue = $"LTRIM(RTRIM(CONVERT(nvarchar(255), cs.[{crseColumn}])))";
            var qualBlank = $"q.[{qualColumn}] IS NULL OR {qualValue} = ''";
            var studBlank = $"s.[{studColumn}] IS NULL OR {studValue} = ''";
            var cregBlank = $"cr.[{cregColumn}] IS NULL OR {cregValue} = ''";

            var rule1Criteria = $"ISBLANK({request.QualTable}.{qualColumn})";
            var rule2Criteria = $"ISBLANK({request.QualTable}.{qualColumn})";
            var rule3Criteria = $"Checked for duplicates on {request.QualTable} using the {qualColumn} field.";
            var rule4Criteria = $"Checked for duplicates on {request.CrseTable} using the {crseColumn} field.";
            var rule5Criteria = $"NOT MAP({request.StudTable}.{studColumn}, \"{EscapeCriteriaText(rule5PlaceholderValue)}\")";
            var rule6Criteria = $"ISBLANK({request.StudTable}.{studColumn})";
            var rule7Criteria = $"ISBLANK({request.StudTable}.{studColumn}) OR {request.StudTable}.{studColumn} <> {request.QualTable}.{qualColumn}";
            var rule8Criteria = $"ISBLANK({request.CregTable}.{cregColumn}) OR {request.CregTable}.{cregColumn} <> {request.CrseTable}.{crseColumn}";
            var rule9Criteria = $"{request.CregTable}.{cregColumn} = {request.StudTable}.{studColumn} AND {request.CregTable}.{rule9CregContextColumnSafe} = {request.StudTable}.{rule9StudContextColumnSafe}";
            var failPreviewTake = Math.Max(BrowserPreviewRowLimit / 2, 1);
            var passPreviewTake = Math.Max(BrowserPreviewRowLimit - failPreviewTake, 1);
            var rule8PrepSql = $@"
DROP TABLE IF EXISTS #Rule8Creg;
DROP TABLE IF EXISTS #Rule8Crse;

SELECT
    CONVERT(nvarchar(255), cr.[{rule8ContextColumnSafe}]) AS STUDENT_COLUMN_VALUE,
    CONVERT(nvarchar(255), cr.[{cregColumn}]) AS LEFT_COLUMN_VALUE,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), cr.[{cregColumn}])))) AS NORMALIZED_JOIN_VALUE,
    CASE
        WHEN cr.[{cregColumn}] IS NULL OR LTRIM(RTRIM(CONVERT(nvarchar(255), cr.[{cregColumn}]))) = ''
            THEN 1
        ELSE 0
    END AS IS_BLANK
INTO #Rule8Creg
FROM [{creg}] cr;

CREATE NONCLUSTERED INDEX IX_Rule8Creg_Normalized
ON #Rule8Creg (NORMALIZED_JOIN_VALUE)
INCLUDE (IS_BLANK, STUDENT_COLUMN_VALUE, LEFT_COLUMN_VALUE);

SELECT
    LTRIM(RTRIM(CONVERT(nvarchar(255), cs.[{crseColumn}]))) AS RIGHT_COLUMN_VALUE,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), cs.[{crseColumn}])))) AS NORMALIZED_JOIN_VALUE
INTO #Rule8Crse
FROM [{crse}] cs
WHERE cs.[{crseColumn}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(nvarchar(255), cs.[{crseColumn}]))) <> '';

CREATE NONCLUSTERED INDEX IX_Rule8Crse_Normalized
ON #Rule8Crse (NORMALIZED_JOIN_VALUE)
INCLUDE (RIGHT_COLUMN_VALUE);";
            var rule8ReviewProjection = $@"SELECT
CASE WHEN cr.IS_BLANK = 1 OR cs.NORMALIZED_JOIN_VALUE IS NULL THEN 'FAIL' ELSE 'PASS' END AS Validation_Result,
CASE
    WHEN cr.IS_BLANK = 1 THEN 'fail because the selected course value is blank in the CREG table'
    WHEN cs.NORMALIZED_JOIN_VALUE IS NULL THEN CONCAT('fail because ', cr.LEFT_COLUMN_VALUE, ' is not in ', '{request.CrseTable}')
    ELSE CONCAT('pass because ', cr.LEFT_COLUMN_VALUE, ' is in both tables')
END AS Validation_Explanation,
CASE WHEN cr.IS_BLANK = 1 OR cs.NORMALIZED_JOIN_VALUE IS NULL THEN 'Invalid Course Reference' ELSE 'Valid Course Reference' END AS Exception_Type,
'{request.CregTable}' AS STUDENT_TABLE_NAME, '{rule8ContextColumnSafe}' AS STUDENT_COLUMN_NAME, cr.STUDENT_COLUMN_VALUE,
'{request.CregTable}' AS LEFT_TABLE_NAME, '{cregColumn}' AS LEFT_COLUMN_NAME, cr.LEFT_COLUMN_VALUE,
'{request.CrseTable}' AS RIGHT_TABLE_NAME, '{crseColumn}' AS RIGHT_COLUMN_NAME, cs.RIGHT_COLUMN_VALUE AS RIGHT_COLUMN_VALUE,
CASE
    WHEN cr.IS_BLANK = 1 THEN 'fail because the selected course value is blank in the CREG table'
    WHEN cs.NORMALIZED_JOIN_VALUE IS NULL THEN CONCAT('fail because ', cr.LEFT_COLUMN_VALUE, ' is not in ', '{request.CrseTable}')
    ELSE CONCAT('pass because ', cr.LEFT_COLUMN_VALUE, ' is in both tables')
END AS FINAL_RESULT_MESSAGE
FROM #Rule8Creg cr
OUTER APPLY
(
    SELECT TOP 1
        s.NORMALIZED_JOIN_VALUE,
        s.RIGHT_COLUMN_VALUE
    FROM #Rule8Crse s
    WHERE s.NORMALIZED_JOIN_VALUE = cr.NORMALIZED_JOIN_VALUE
) cs";
            var rule8ReviewSql = $@"{rule8ReviewProjection}
ORDER BY CASE WHEN cr.IS_BLANK = 1 OR cs.NORMALIZED_JOIN_VALUE IS NULL THEN 0 ELSE 1 END, cr.STUDENT_COLUMN_VALUE;";
            var rule9PrepSql = $@"
DROP TABLE IF EXISTS #Rule9Creg;
DROP TABLE IF EXISTS #Rule9StudRaw;
DROP TABLE IF EXISTS #Rule9Stud;

SELECT
    CONVERT(nvarchar(255), cr.[{cregColumn}]) AS LEFT_STUDENT_COLUMN_VALUE,
    CONVERT(nvarchar(255), cr.[{rule9CregContextColumnSafe}]) AS LEFT_COLUMN_VALUE,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), cr.[{cregColumn}])))) AS NORMALIZED_JOIN_VALUE,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), cr.[{rule9CregContextColumnSafe}])))) AS NORMALIZED_CONTEXT_VALUE,
    CASE
        WHEN cr.[{cregColumn}] IS NULL OR LTRIM(RTRIM(CONVERT(nvarchar(255), cr.[{cregColumn}]))) = ''
            THEN 1
        ELSE 0
    END AS IS_STUDENT_BLANK,
    CASE
        WHEN cr.[{rule9CregContextColumnSafe}] IS NULL OR LTRIM(RTRIM(CONVERT(nvarchar(255), cr.[{rule9CregContextColumnSafe}]))) = ''
            THEN 1
        ELSE 0
    END AS IS_CONTEXT_BLANK
INTO #Rule9Creg
FROM [{creg}] cr;

CREATE NONCLUSTERED INDEX IX_Rule9Creg_Normalized
ON #Rule9Creg (NORMALIZED_JOIN_VALUE, NORMALIZED_CONTEXT_VALUE)
INCLUDE (IS_STUDENT_BLANK, IS_CONTEXT_BLANK, LEFT_STUDENT_COLUMN_VALUE, LEFT_COLUMN_VALUE);

SELECT
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), s.[{studColumn}])))) AS NORMALIZED_JOIN_VALUE,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), s.[{rule9StudContextColumnSafe}])))) AS NORMALIZED_CONTEXT_VALUE,
    LTRIM(RTRIM(CONVERT(nvarchar(255), s.[{studColumn}]))) AS RIGHT_STUDENT_COLUMN_VALUE,
    LTRIM(RTRIM(CONVERT(nvarchar(255), s.[{rule9StudContextColumnSafe}]))) AS RIGHT_COLUMN_VALUE
INTO #Rule9Stud
FROM [{stud}] s
WHERE s.[{studColumn}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(nvarchar(255), s.[{studColumn}]))) <> '';

CREATE NONCLUSTERED INDEX IX_Rule9Stud_Normalized
ON #Rule9Stud (NORMALIZED_JOIN_VALUE, NORMALIZED_CONTEXT_VALUE)
INCLUDE (RIGHT_STUDENT_COLUMN_VALUE, RIGHT_COLUMN_VALUE);";
            var rule9ReviewProjection = $@"SELECT
CASE
    WHEN cr.IS_STUDENT_BLANK = 1 OR cr.IS_CONTEXT_BLANK = 1 OR studentMatch.NORMALIZED_JOIN_VALUE IS NULL OR exactMatch.NORMALIZED_JOIN_VALUE IS NULL THEN 'FAIL'
    ELSE 'PASS'
END AS Validation_Result,
CASE
    WHEN cr.IS_STUDENT_BLANK = 1 THEN 'fail because the selected student value is blank in the CREG table'
    WHEN cr.IS_CONTEXT_BLANK = 1 THEN 'fail because the selected course value is blank in the CREG table'
    WHEN studentMatch.NORMALIZED_JOIN_VALUE IS NULL THEN CONCAT('fail because student number ', cr.LEFT_STUDENT_COLUMN_VALUE, ' is missing in ', '{request.StudTable}')
    WHEN exactMatch.NORMALIZED_JOIN_VALUE IS NULL THEN CONCAT('fail because student number ', cr.LEFT_STUDENT_COLUMN_VALUE, ' exists in both tables but course value ', cr.LEFT_COLUMN_VALUE, ' from ', '{request.CregTable}', ' does not match ', '{request.StudTable}')
    ELSE CONCAT('pass because student number ', cr.LEFT_STUDENT_COLUMN_VALUE, ' is in both tables and course value ', cr.LEFT_COLUMN_VALUE, ' matches')
END AS Validation_Explanation,
CASE
    WHEN cr.IS_STUDENT_BLANK = 1 OR cr.IS_CONTEXT_BLANK = 1 OR studentMatch.NORMALIZED_JOIN_VALUE IS NULL OR exactMatch.NORMALIZED_JOIN_VALUE IS NULL
        THEN 'Ghost Student Registration'
    ELSE 'Valid Student Registration'
END AS Exception_Type,
'{request.CregTable}' AS LEFT_TABLE_NAME, '{cregColumn}' AS LEFT_STUDENT_COLUMN_NAME, cr.LEFT_STUDENT_COLUMN_VALUE AS LEFT_STUDENT_COLUMN_VALUE,
'{request.CregTable}' AS LEFT_CONTEXT_TABLE_NAME, '{rule9CregContextColumnSafe}' AS LEFT_COLUMN_NAME, cr.LEFT_COLUMN_VALUE AS LEFT_COLUMN_VALUE,
'{request.StudTable}' AS RIGHT_TABLE_NAME, '{studColumn}' AS RIGHT_STUDENT_COLUMN_NAME, COALESCE(exactMatch.RIGHT_STUDENT_COLUMN_VALUE, studentMatch.RIGHT_STUDENT_COLUMN_VALUE) AS RIGHT_STUDENT_COLUMN_VALUE,
'{request.StudTable}' AS RIGHT_CONTEXT_TABLE_NAME, '{rule9StudContextColumnSafe}' AS RIGHT_COLUMN_NAME, COALESCE(exactMatch.RIGHT_COLUMN_VALUE, studentMatch.RIGHT_COLUMN_VALUE) AS RIGHT_COLUMN_VALUE,
CASE
    WHEN cr.IS_STUDENT_BLANK = 1 THEN 'fail because the selected student value is blank in the CREG table'
    WHEN cr.IS_CONTEXT_BLANK = 1 THEN 'fail because the selected course value is blank in the CREG table'
    WHEN studentMatch.NORMALIZED_JOIN_VALUE IS NULL THEN CONCAT('fail because student number ', cr.LEFT_STUDENT_COLUMN_VALUE, ' is missing in ', '{request.StudTable}')
    WHEN exactMatch.NORMALIZED_JOIN_VALUE IS NULL THEN CONCAT('fail because student number ', cr.LEFT_STUDENT_COLUMN_VALUE, ' exists in both tables but course value ', cr.LEFT_COLUMN_VALUE, ' from ', '{request.CregTable}', ' does not match ', '{request.StudTable}')
    ELSE CONCAT('pass because student number ', cr.LEFT_STUDENT_COLUMN_VALUE, ' is in both tables and course value ', cr.LEFT_COLUMN_VALUE, ' matches')
END AS FINAL_RESULT_MESSAGE
FROM #Rule9Creg cr
OUTER APPLY
(
    SELECT TOP 1
        st.NORMALIZED_JOIN_VALUE,
        st.RIGHT_STUDENT_COLUMN_VALUE,
        st.RIGHT_COLUMN_VALUE
    FROM #Rule9Stud st
    WHERE st.NORMALIZED_JOIN_VALUE = cr.NORMALIZED_JOIN_VALUE
) studentMatch
OUTER APPLY
(
    SELECT TOP 1
        st.NORMALIZED_JOIN_VALUE,
        st.RIGHT_STUDENT_COLUMN_VALUE,
        st.RIGHT_COLUMN_VALUE
    FROM #Rule9Stud st
    WHERE st.NORMALIZED_JOIN_VALUE = cr.NORMALIZED_JOIN_VALUE
      AND st.NORMALIZED_CONTEXT_VALUE = cr.NORMALIZED_CONTEXT_VALUE
) exactMatch";
            var rule9ReviewSql = $@"{rule9ReviewProjection}
ORDER BY CASE WHEN cr.IS_STUDENT_BLANK = 1 OR cr.IS_CONTEXT_BLANK = 1 OR studentMatch.NORMALIZED_JOIN_VALUE IS NULL OR exactMatch.NORMALIZED_JOIN_VALUE IS NULL THEN 0 ELSE 1 END, cr.LEFT_STUDENT_COLUMN_VALUE;";

            var definitions = new List<IntegrityRuleDefinition>
            {
                new(1, "Qualifications without qualification type", rule1Criteria, request.QualTable, "High",
                    $@"SELECT COUNT(*) FROM [{qual}] q WHERE {qualBlank};",
                    $@"SELECT COUNT(*) FROM [{qual}] q;",
                    $@"SELECT
CASE WHEN {qualBlank} THEN 'FAIL' ELSE 'PASS' END AS Validation_Result,
CASE WHEN {qualBlank}
    THEN 'The selected value is blank on the selected table and column.'
    ELSE 'The selected value is populated on the selected table and column.'
END AS Validation_Explanation,
CASE WHEN {qualBlank} THEN 'Missing Qualification Type' ELSE 'Populated Qualification Type' END AS Exception_Type,
'{request.QualTable}' AS TABLE_NAME, '{qualColumn}' AS COLUMN_NAME, CONVERT(nvarchar(255), q.[{qualColumn}]) AS COLUMN_VALUE
FROM [{qual}] q
ORDER BY CASE WHEN {qualBlank} THEN 0 ELSE 1 END, CONVERT(nvarchar(255), q.[{qualColumn}]);"),
                new(2, "Qualifications without approval status", rule2Criteria, request.QualTable, "High",
                    $@"SELECT COUNT(*) FROM [{qual}] q WHERE {qualBlank};",
                    $@"SELECT COUNT(*) FROM [{qual}] q;",
                    $@"SELECT
CASE WHEN {qualBlank} THEN 'FAIL' ELSE 'PASS' END AS Validation_Result,
CASE WHEN {qualBlank}
    THEN 'The selected value is blank on the selected table and column.'
    ELSE 'The selected value is populated on the selected table and column.'
END AS Validation_Explanation,
CASE WHEN {qualBlank} THEN 'Missing Approval Status' ELSE 'Populated Approval Status' END AS Exception_Type,
'{request.QualTable}' AS TABLE_NAME, '{qualColumn}' AS COLUMN_NAME, CONVERT(nvarchar(255), q.[{qualColumn}]) AS COLUMN_VALUE
FROM [{qual}] q
ORDER BY CASE WHEN {qualBlank} THEN 0 ELSE 1 END, CONVERT(nvarchar(255), q.[{qualColumn}]);"),
                new(3, "Duplicate qualification codes", rule3Criteria, request.QualTable, "High",
                    $@"SELECT COUNT(*)
FROM
(
    SELECT [{qualColumn}] AS DuplicateValue
    FROM [{qual}]
    GROUP BY [{qualColumn}]
    HAVING COUNT(*) > 1
) d;",
                    $@"SELECT COUNT(*)
FROM
(
    SELECT [{qualColumn}]
    FROM [{qual}]
    GROUP BY [{qualColumn}]
) d;",
                    $@"SELECT
CASE WHEN COUNT(*) > 1 THEN 'FAIL' ELSE 'PASS' END AS Validation_Result,
CASE WHEN COUNT(*) > 1
    THEN CONCAT('fail because ', CONVERT(nvarchar(4000), [{qualColumn}]), ' appears more than once in the selected column')
    ELSE CONCAT('pass because ', CONVERT(nvarchar(4000), [{qualColumn}]), ' appears once in the selected column')
END AS Validation_Explanation,
CASE WHEN COUNT(*) > 1 THEN 'Duplicate Qualification Code' ELSE 'Unique Qualification Code' END AS Exception_Type,
'{request.QualTable}' AS TABLE_NAME, '{qualColumn}' AS COLUMN_NAME,
CONVERT(nvarchar(4000), [{qualColumn}]) AS COLUMN_VALUE,
CONVERT(nvarchar(4000), [{qualColumn}]) AS DUPLICATE_VALUE,
COUNT(*) AS DUPLICATE_COUNT
FROM [{qual}]
GROUP BY [{qualColumn}]
ORDER BY COUNT(*) DESC, CONVERT(nvarchar(4000), [{qualColumn}]);"),
                new(4, "Duplicate course codes", rule4Criteria, request.CrseTable, "High",
                    $@"SELECT COUNT(*)
FROM
(
    SELECT [{crseColumn}] AS DuplicateValue
    FROM [{crse}]
    GROUP BY [{crseColumn}]
    HAVING COUNT(*) > 1
) d;",
                    $@"SELECT COUNT(*)
FROM
(
    SELECT [{crseColumn}]
    FROM [{crse}]
    GROUP BY [{crseColumn}]
) d;",
                    $@"SELECT
CASE WHEN COUNT(*) > 1 THEN 'FAIL' ELSE 'PASS' END AS Validation_Result,
CASE WHEN COUNT(*) > 1
    THEN CONCAT('fail because ', CONVERT(nvarchar(4000), [{crseColumn}]), ' appears more than once in the selected column')
    ELSE CONCAT('pass because ', CONVERT(nvarchar(4000), [{crseColumn}]), ' appears once in the selected column')
END AS Validation_Explanation,
CASE WHEN COUNT(*) > 1 THEN 'Duplicate Course Code' ELSE 'Unique Course Code' END AS Exception_Type,
'{request.CrseTable}' AS TABLE_NAME, '{crseColumn}' AS COLUMN_NAME,
CONVERT(nvarchar(4000), [{crseColumn}]) AS COLUMN_VALUE,
CONVERT(nvarchar(4000), [{crseColumn}]) AS DUPLICATE_VALUE,
COUNT(*) AS DUPLICATE_COUNT
FROM [{crse}]
GROUP BY [{crseColumn}]
ORDER BY COUNT(*) DESC, CONVERT(nvarchar(4000), [{crseColumn}]);"),
                new(5, "Invalid student numbers", rule5Criteria, request.StudTable, "High",
                    $@"SELECT COUNT(*) FROM [{stud}] s
WHERE CONVERT(nvarchar(255), s.[{studColumn}]) = {rule5PlaceholderValueSql};",
                    $@"SELECT COUNT(*) FROM [{stud}] s;",
                    $@"SELECT
CASE WHEN CONVERT(nvarchar(255), s.[{studColumn}]) = {rule5PlaceholderValueSql} THEN 'FAIL' ELSE 'PASS' END AS Validation_Result,
CASE WHEN CONVERT(nvarchar(255), s.[{studColumn}]) = {rule5PlaceholderValueSql}
    THEN CONCAT('fail because the selected value matches the configured invalid value ', {rule5PlaceholderValueSql})
    ELSE CONCAT('pass because the selected value does not match the configured invalid value ', {rule5PlaceholderValueSql})
END AS Validation_Explanation,
CASE WHEN CONVERT(nvarchar(255), s.[{studColumn}]) = {rule5PlaceholderValueSql} THEN 'Invalid Student Number' ELSE 'Valid Student Number' END AS Exception_Type,
'{request.StudTable}' AS TABLE_NAME, '{studColumn}' AS COLUMN_NAME, CONVERT(nvarchar(255), s.[{studColumn}]) AS COLUMN_VALUE, {rule5PlaceholderValueSql} AS EXPECTED_VALUE
FROM [{stud}] s
ORDER BY CASE WHEN CONVERT(nvarchar(255), s.[{studColumn}]) = {rule5PlaceholderValueSql} THEN 0 ELSE 1 END, CONVERT(nvarchar(255), s.[{studColumn}]);"),
                new(6, "Students without foundation indicator", rule6Criteria, request.StudTable, "Medium",
                    $@"SELECT COUNT(*) FROM [{stud}] s WHERE {studBlank};",
                    $@"SELECT COUNT(*) FROM [{stud}] s;",
                    $@"SELECT
CASE WHEN {studBlank} THEN 'FAIL' ELSE 'PASS' END AS Validation_Result,
CASE WHEN {studBlank}
    THEN 'The selected value is blank on the selected table and column.'
    ELSE 'The selected value is populated on the selected table and column.'
END AS Validation_Explanation,
CASE WHEN {studBlank} THEN 'Missing Foundation Indicator' ELSE 'Populated Foundation Indicator' END AS Exception_Type,
'{request.StudTable}' AS TABLE_NAME, '{studColumn}' AS COLUMN_NAME, CONVERT(nvarchar(255), s.[{studColumn}]) AS COLUMN_VALUE
FROM [{stud}] s
ORDER BY CASE WHEN {studBlank} THEN 0 ELSE 1 END, CONVERT(nvarchar(255), s.[{studColumn}]);"),
                new(7, "Students with invalid qualifications", rule7Criteria, $"{request.StudTable} -> {request.QualTable}", "High",
                    $@"SELECT COUNT(*) FROM [{stud}] s
LEFT JOIN [{qual}] q ON NOT ({studBlank}) AND UPPER({studValue}) = UPPER({qualValue})
WHERE {studBlank} OR q.[{qualColumn}] IS NULL;",
                    $@"SELECT COUNT(*) FROM [{stud}] s;",
                    $@"SELECT
CASE WHEN {studBlank} OR q.[{qualColumn}] IS NULL THEN 'FAIL' ELSE 'PASS' END AS Validation_Result,
CASE
    WHEN {studBlank} THEN 'fail because the selected qualification value is blank in the STUD table'
    WHEN q.[{qualColumn}] IS NULL THEN CONCAT('fail because ', CONVERT(nvarchar(255), s.[{studColumn}]), ' is not in ', '{request.QualTable}')
    ELSE CONCAT('pass because ', CONVERT(nvarchar(255), s.[{studColumn}]), ' is in both tables')
END AS Validation_Explanation,
CASE WHEN {studBlank} OR q.[{qualColumn}] IS NULL THEN 'Invalid Qualification Reference' ELSE 'Valid Qualification Reference' END AS Exception_Type,
'{request.StudTable}' AS STUDENT_TABLE_NAME, '{rule7ContextColumnSafe}' AS STUDENT_COLUMN_NAME, CONVERT(nvarchar(255), s.[{rule7ContextColumnSafe}]) AS STUDENT_COLUMN_VALUE,
'{request.StudTable}' AS LEFT_TABLE_NAME, '{studColumn}' AS LEFT_COLUMN_NAME, CONVERT(nvarchar(255), s.[{studColumn}]) AS LEFT_COLUMN_VALUE,
'{request.QualTable}' AS RIGHT_TABLE_NAME, '{qualColumn}' AS RIGHT_COLUMN_NAME, LTRIM(RTRIM(CONVERT(nvarchar(255), q.[{qualColumn}]))) AS RIGHT_COLUMN_VALUE,
CASE
    WHEN {studBlank} THEN 'fail because the selected qualification value is blank in the STUD table'
    WHEN q.[{qualColumn}] IS NULL THEN CONCAT('fail because ', CONVERT(nvarchar(255), s.[{studColumn}]), ' is not in ', '{request.QualTable}')
    ELSE CONCAT('pass because ', CONVERT(nvarchar(255), s.[{studColumn}]), ' is in both tables')
END AS FINAL_RESULT_MESSAGE
FROM [{stud}] s
LEFT JOIN [{qual}] q ON NOT ({studBlank}) AND UPPER({studValue}) = UPPER({qualValue})
ORDER BY CASE WHEN {studBlank} OR q.[{qualColumn}] IS NULL THEN 0 ELSE 1 END, CONVERT(nvarchar(255), s.[{rule7ContextColumnSafe}]);"),
                new(8, "Course registrations for invalid courses", rule8Criteria, $"{request.CregTable} -> {request.CrseTable}", "High",
                    @"SELECT COUNT(*)
FROM #Rule8Creg cr
WHERE cr.IS_BLANK = 1
   OR NOT EXISTS
   (
       SELECT 1
       FROM #Rule8Crse cs
       WHERE cs.NORMALIZED_JOIN_VALUE = cr.NORMALIZED_JOIN_VALUE
   );",
                    @"SELECT COUNT(*) FROM #Rule8Creg;",
                    rule8ReviewSql,
                    null,
                    rule8PrepSql),
                new(9, "Course registrations for ghost students", rule9Criteria, $"{request.CregTable} -> {request.StudTable}", "High",
                    @"SELECT COUNT(*)
FROM #Rule9Creg cr
WHERE cr.IS_STUDENT_BLANK = 1
   OR cr.IS_CONTEXT_BLANK = 1
   OR NOT EXISTS
   (
       SELECT 1
       FROM #Rule9Stud s
       WHERE s.NORMALIZED_JOIN_VALUE = cr.NORMALIZED_JOIN_VALUE
   )
   OR NOT EXISTS
   (
       SELECT 1
       FROM #Rule9Stud s
       WHERE s.NORMALIZED_JOIN_VALUE = cr.NORMALIZED_JOIN_VALUE
         AND s.NORMALIZED_CONTEXT_VALUE = cr.NORMALIZED_CONTEXT_VALUE
   );",
                    @"SELECT COUNT(*) FROM #Rule9Creg;",
                    rule9ReviewSql,
                    null,
                    rule9PrepSql),
                new(10, "Joining Rules", "The tables were joined on the documented key fields for each dataset.", "Joining Rules", "Medium",
                    "SELECT 0;",
                    "SELECT 0;",
                    @"SELECT TOP 0
'INFO' AS Validation_Result,
'Joining rules reference only.' AS Validation_Explanation,
'Joining Rules' AS Exception_Type;")
            };

            return definitions
                .Where(definition => definition.RuleId == request.RuleNumber)
                .ToList();
        }

        private async Task<List<Rule10ValidationRowRecord>> LoadValidationRowsAsync(SqlConnection connection, IntegrityRuleDefinition definition, int? maxRows)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = maxRows.HasValue && maxRows.Value > 0 && !string.IsNullOrWhiteSpace(definition.SampleReviewSql)
                ? definition.SampleReviewSql
                : BuildReviewSql(definition.ReviewSql, maxRows);

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule10ValidationRowRecord>();
            while (await reader.ReadAsync())
            {
                var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    displayValues[reader.GetName(i)] = reader.IsDBNull(i)
                        ? null
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                }

                var row = new Rule10ValidationRowRecord
                {
                    ValidationNumber = rows.Count + 1,
                    RuleId = definition.RuleId,
                    ControlType = $"Rule_{definition.RuleId}",
                    ControlLabel = $"Rule {definition.RuleId}",
                    ValidationResult = ReadValue(displayValues, "Validation_Result"),
                    ValidationExplanation = ReadValue(displayValues, "Validation_Explanation"),
                    DisplayValues = displayValues
                };
                EnrichRule10DisplayValues(row);
                rows.Add(row);
            }

            return rows;
        }

        private static string BuildReviewSql(string reviewSql, int? maxRows)
        {
            if (!maxRows.HasValue || maxRows.Value <= 0)
                return reviewSql;

            var trimmed = reviewSql.Trim();
            var orderByIndex = trimmed.LastIndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
            if (orderByIndex >= 0)
                trimmed = trimmed[..orderByIndex].TrimEnd().TrimEnd(';');

            var failTake = Math.Max(maxRows.Value / 2, 1);
            var passTake = Math.Max(maxRows.Value - failTake, 1);

            return $@"
SELECT *
FROM
(
    SELECT TOP {failTake} *
    FROM
    (
        {trimmed}
    ) fail_sample
    WHERE UPPER(ISNULL(Validation_Result, '')) = 'FAIL'
) fails
UNION ALL
SELECT *
FROM
(
    SELECT TOP {passTake} *
    FROM
    (
        {trimmed}
    ) pass_sample
    WHERE UPPER(ISNULL(Validation_Result, '')) = 'PASS'
) passes;";
        }

        private async Task EnsureColumnsExistAsync(string server, string database, string driver, string qualTable, string studTable, string cregTable, string crseTable)
        {
            var qualColumns = await GetTableColumnsAsync(server, database, driver, qualTable);
            var studColumns = await GetTableColumnsAsync(server, database, driver, studTable);
            var cregColumns = await GetTableColumnsAsync(server, database, driver, cregTable);
            var crseColumns = await GetTableColumnsAsync(server, database, driver, crseTable);

            EnsureHasColumns(qualTable, qualColumns, "_001", "_002", "_003", "_004", "_005");
            EnsureHasColumns(studTable, studColumns, "_001", "_007", "_019", "_024", "_025", "_106");
            EnsureHasColumns(cregTable, cregColumns, "_001", "_007", "_030", "_032");
            EnsureHasColumns(crseTable, crseColumns, "_030");
        }

        private async Task EnsureColumnsExistAsync(Rule10ValidationRequest request)
        {
            await EnsureColumnsExistAsyncForRule(request);
        }

        private async Task EnsureColumnsExistAsync(Rule10VerifyRequest request)
        {
            await EnsureColumnsExistAsyncForRule(request);
        }

        private async Task EnsureColumnsExistAsyncForRule(Rule10ValidationRequest request)
        {
            switch (request.RuleNumber)
            {
                case 1:
                case 2:
                case 3:
                    EnsureHasColumns(
                        request.QualTable,
                        await GetTableColumnsAsync(request.Server, request.Database, request.Driver, request.QualTable),
                        ResolveSelectedColumn(request.QualColumn));
                    break;
                case 4:
                    EnsureHasColumns(
                        request.CrseTable,
                        await GetTableColumnsAsync(request.Server, request.Database, request.Driver, request.CrseTable),
                        ResolveSelectedColumn(request.CrseColumn));
                    break;
                case 5:
                case 6:
                    EnsureHasColumns(
                        request.StudTable,
                        await GetTableColumnsAsync(request.Server, request.Database, request.Driver, request.StudTable),
                        ResolveSelectedColumn(request.StudColumn));
                    break;
                case 7:
                    EnsureHasColumns(
                        request.StudTable,
                        await GetTableColumnsAsync(request.Server, request.Database, request.Driver, request.StudTable),
                        ResolveSelectedColumn(request.StudColumn),
                        ResolveSelectedColumn(ParseRuleParameters(request.RuleParameterJson, request.RuleNumber).ContextColumn));
                    EnsureHasColumns(
                        request.QualTable,
                        await GetTableColumnsAsync(request.Server, request.Database, request.Driver, request.QualTable),
                        ResolveSelectedColumn(request.QualColumn));
                    break;
                case 8:
                    EnsureHasColumns(
                        request.CregTable,
                        await GetTableColumnsAsync(request.Server, request.Database, request.Driver, request.CregTable),
                        ResolveSelectedColumn(request.CregColumn),
                        ResolveSelectedColumn(ParseRuleParameters(request.RuleParameterJson, request.RuleNumber).ContextColumn));
                    EnsureHasColumns(
                        request.CrseTable,
                        await GetTableColumnsAsync(request.Server, request.Database, request.Driver, request.CrseTable),
                        ResolveSelectedColumn(request.CrseColumn));
                    break;
                case 9:
                    EnsureHasColumns(
                        request.CregTable,
                        await GetTableColumnsAsync(request.Server, request.Database, request.Driver, request.CregTable),
                        ResolveSelectedColumn(request.CregColumn),
                        ResolveSelectedColumn(ParseRuleParameters(request.RuleParameterJson, request.RuleNumber).ContextColumn));
                    EnsureHasColumns(
                        request.StudTable,
                        await GetTableColumnsAsync(request.Server, request.Database, request.Driver, request.StudTable),
                        ResolveSelectedColumn(request.StudColumn),
                        ResolveSelectedColumn(ParseRuleParameters(request.RuleParameterJson, request.RuleNumber).SecondaryContextColumn));
                    break;
                case 10:
                    await VerifyRule10JoinDatasetsAsync(
                        request.Server,
                        request.Database,
                        request.Driver,
                        request.Rule10JoinConfigJson,
                        throwOnMissingColumns: true);
                    break;
                default:
                    throw new InvalidOperationException($"Integrity rule {request.RuleNumber} is not supported.");
            }
        }

        private async Task EnsureColumnsExistAsyncForRule(Rule10VerifyRequest request)
        {
            await EnsureColumnsExistAsyncForRule(new Rule10ValidationRequest
            {
                RuleNumber = request.RuleNumber,
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                QualTable = request.QualTable,
                StudTable = request.StudTable,
                CregTable = request.CregTable,
                CrseTable = request.CrseTable,
                QualColumn = request.QualColumn,
                StudColumn = request.StudColumn,
                CregColumn = request.CregColumn,
                CrseColumn = request.CrseColumn,
                Rule10JoinConfigJson = request.Rule10JoinConfigJson
            });
        }

        private async Task<List<Rule10JoinDatasetVerificationItem>> VerifyRule10JoinDatasetsAsync(string server, string database, string driver, string? joinConfigJson, bool throwOnMissingColumns)
        {
            var results = new List<Rule10JoinDatasetVerificationItem>();
            foreach (var dataset in ResolveRule10JoinDatasets(joinConfigJson))
            {
                var columns = await GetTableColumnsAsync(server, database, driver, dataset.TableName);
                var missingColumns = dataset.KeyColumns
                    .Where(column => !columns.Contains(column, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                var recordCount = missingColumns.Count == 0
                    ? await CountRowsAsync(server, database, driver, dataset.TableName)
                    : 0;

                results.Add(new Rule10JoinDatasetVerificationItem
                {
                    DatasetCode = dataset.DatasetCode,
                    DatasetLabel = dataset.DatasetLabel,
                    TableName = dataset.TableName,
                    RecordCount = recordCount,
                    RequiredColumns = dataset.KeyColumns.ToList(),
                    MissingColumns = missingColumns,
                    Status = missingColumns.Count == 0 ? "PASS" : "FAIL"
                });
            }

            var firstFailure = results.FirstOrDefault(item => item.MissingColumns.Count > 0);
            if (throwOnMissingColumns && firstFailure != null)
            {
                throw new InvalidOperationException(
                    $"Table {firstFailure.TableName} is missing required key column(s): {string.Join(", ", firstFailure.MissingColumns)}.");
            }

            return results;
        }

        private async Task<int> CountRowsAsync(string server, string database, string driver, string tableName)
        {
            var connStr = BuildConnectionString(server, database, driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            return await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(tableName)}];");
        }

        private async Task<Rule10ValidationSummary> AnalyseRule10JoinVerificationAsync(Rule10ValidationRequest request)
        {
            var rule = IntegrityRuleCatalog.Get(request.RuleNumber);
            var verifications = await VerifyRule10JoinDatasetsAsync(
                request.Server,
                request.Database,
                request.Driver,
                request.Rule10JoinConfigJson,
                throwOnMissingColumns: false);

            var summaries = verifications.Select((item, index) => new Rule10ControlSummaryItemViewModel
            {
                RuleId = index + 1,
                ControlType = item.DatasetCode,
                ControlLabel = item.DatasetLabel,
                CriteriaText = $"Key fields: {string.Join(", ", item.RequiredColumns)}{(item.TableName.Length > 0 ? $" | Selected table: {item.TableName}" : string.Empty)}",
                TableName = item.TableName,
                Severity = "Medium",
                ErrorCount = item.MissingColumns.Count,
                RequestedCount = item.RequiredColumns.Count,
                AvailableCount = item.RequiredColumns.Count - item.MissingColumns.Count,
                AchievedCount = item.MissingColumns.Count == 0 ? 1 : 0,
                TotalCount = 1,
                PassCount = item.MissingColumns.Count == 0 ? 1 : 0,
                FailCount = item.MissingColumns.Count > 0 ? 1 : 0,
                Status = item.MissingColumns.Count == 0 ? "PASS" : "FAIL"
            }).ToList();

            var reviewRows = new List<Rule10ValidationRowRecord>();
            for (var i = 0; i < verifications.Count; i++)
            {
                var item = verifications[i];
                var isFailure = item.MissingColumns.Count > 0;
                reviewRows.Add(new Rule10ValidationRowRecord
                {
                    ValidationNumber = i + 1,
                    RuleId = 10,
                    ControlType = item.DatasetCode,
                    ControlLabel = item.DatasetLabel,
                    ValidationResult = isFailure ? "FAIL" : "PASS",
                    ValidationExplanation = isFailure
                        ? "The documented key columns do not all exist on the selected table."
                        : "All documented key columns exist on the selected table.",
                    DisplayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["DATASET"] = item.DatasetLabel,
                        ["TABLE_NAME"] = item.TableName,
                        ["REQUIRED_KEY_COLUMNS"] = string.Join(", ", item.RequiredColumns),
                        ["MISSING_COLUMNS"] = string.Join(", ", item.MissingColumns),
                        ["FINAL_RESULT_MESSAGE"] = isFailure
                            ? $"fail because {string.Join(", ", item.MissingColumns)} {(item.MissingColumns.Count == 1 ? "is" : "are")} missing on {item.TableName}"
                            : $"pass because all documented key columns exist on {item.TableName}"
                    }
                });
            }

            var passedChecks = summaries.Count(item => string.Equals(item.Status, "PASS", StringComparison.OrdinalIgnoreCase));
            var failedChecks = summaries.Count - passedChecks;
            var totalIssues = summaries.Sum(item => item.ErrorCount);

            return new Rule10ValidationSummary
            {
                Success = true,
                RuleNumber = rule.RuleNumber,
                RuleLabel = rule.RuleLabel,
                RuleTitle = rule.RuleTitle,
                TotalChecks = summaries.Count,
                PassedChecks = passedChecks,
                FailedChecks = failedChecks,
                TotalIssues = totalIssues,
                HighSeverityCount = 0,
                TotalRequested = summaries.Count,
                TotalValidated = summaries.Count,
                DisplayedCount = reviewRows.Count,
                IsPreviewOnly = false,
                PreviewLimit = 0,
                PassCount = passedChecks,
                FailCount = failedChecks,
                ExceptionRate = summaries.Count == 0 ? 0m : Math.Round(failedChecks * 100m / summaries.Count, 2),
                Status = failedChecks == 0 ? "PASS" : "FAIL",
                OverallStatusText = failedChecks == 0 ? "EXCELLENT" : "ATTENTION REQUIRED",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                QualTable = request.QualTable,
                StudTable = request.StudTable,
                CregTable = request.CregTable,
                CrseTable = request.CrseTable,
                QualColumn = request.QualColumn,
                StudColumn = request.StudColumn,
                CregColumn = request.CregColumn,
                CrseColumn = request.CrseColumn,
                RuleParameterJson = request.RuleParameterJson,
                Rule10JoinConfigJson = request.Rule10JoinConfigJson,
                TableLinkageText = "Documented dataset key-column existence check",
                RuleModeText = $"{rule.RuleLabel} join-key verification",
                ProcedureSteps = BuildProcedureSteps(request),
                ClientId = request.ClientId,
                ControlSummaries = summaries,
                ReviewRows = reviewRows,
                Warning = failedChecks == 0
                    ? "All documented dataset key columns were found on the selected tables."
                    : "One or more documented dataset key columns are missing from the selected tables."
            };
        }

        private async Task<List<string>> GetTableColumnsAsync(string server, string database, string driver, string tableName)
        {
            ValidateObjectName(tableName);
            var (tableSchema, objectName) = SplitSchemaAndTableName(tableName);
            var fullTableName = tableName.Trim();

            var connStr = BuildConnectionString(server, database, driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN (@FullTableName, @ObjectName)
  AND (
        TABLE_NAME = @FullTableName
        OR @TableSchema = ''
        OR TABLE_SCHEMA = @TableSchema
      )
ORDER BY ORDINAL_POSITION;";
            cmd.Parameters.AddWithValue("@FullTableName", fullTableName);
            cmd.Parameters.AddWithValue("@ObjectName", objectName);
            cmd.Parameters.AddWithValue("@TableSchema", tableSchema);

            await using var reader = await cmd.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                {
                    var columnName = reader.GetString(0);
                    if (!columns.Contains(columnName, StringComparer.OrdinalIgnoreCase))
                        columns.Add(columnName);
                }
            }

            if (columns.Count > 0)
                return columns;

            await reader.CloseAsync();

            await using var fallback = conn.CreateConfiguredCommand();
            fallback.CommandText = @"
SELECT c.name
FROM sys.columns c
INNER JOIN sys.tables t ON t.object_id = c.object_id
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name IN (@FullTableName, @ObjectName)
  AND (
        t.name = @FullTableName
        OR @TableSchema = ''
        OR s.name = @TableSchema
      )
ORDER BY c.column_id;";
            fallback.Parameters.AddWithValue("@FullTableName", fullTableName);
            fallback.Parameters.AddWithValue("@ObjectName", objectName);
            fallback.Parameters.AddWithValue("@TableSchema", tableSchema);

            await using var fallbackReader = await fallback.ExecuteReaderAsync();
            while (await fallbackReader.ReadAsync())
            {
                if (!fallbackReader.IsDBNull(0))
                {
                    var columnName = fallbackReader.GetString(0);
                    if (!columns.Contains(columnName, StringComparer.OrdinalIgnoreCase))
                        columns.Add(columnName);
                }
            }

            return columns;
        }

        private static void EnsureHasColumns(string tableName, IReadOnlyCollection<string> availableColumns, params string[] requiredColumns)
        {
            var missing = requiredColumns
                .Where(required => !string.IsNullOrWhiteSpace(required))
                .Select(required => ResolveSelectedColumn(required))
                .Where(required => !availableColumns.Contains(required, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (missing.Count > 0)
                throw new InvalidOperationException($"Table {tableName} is missing required column(s): {string.Join(", ", missing)}.");
        }

        private static void ValidateRequest(Rule10ValidationRequest request)
        {
            if (!IntegrityRuleCatalog.IsSupported(request.RuleNumber))
                throw new InvalidOperationException($"Integrity rule {request.RuleNumber} is not supported.");
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            ValidateRequiredTables(request);
        }

        private static void ValidateRequest(Rule10VerifyRequest request)
        {
            if (!IntegrityRuleCatalog.IsSupported(request.RuleNumber))
                throw new InvalidOperationException($"Integrity rule {request.RuleNumber} is not supported.");
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            ValidateRequiredTables(new Rule10ValidationRequest
            {
                RuleNumber = request.RuleNumber,
                QualTable = request.QualTable,
                StudTable = request.StudTable,
                CregTable = request.CregTable,
                CrseTable = request.CrseTable,
                QualColumn = request.QualColumn,
                StudColumn = request.StudColumn,
                CregColumn = request.CregColumn,
                CrseColumn = request.CrseColumn,
                Rule10JoinConfigJson = request.Rule10JoinConfigJson
            });
        }

        private static void ValidateRequiredTables(Rule10ValidationRequest request)
        {
            switch (request.RuleNumber)
            {
                case 1:
                    ValidateObjectName(request.QualTable);
                    ValidateObjectName(ResolveSelectedColumn(request.QualColumn));
                    break;
                case 2:
                case 3:
                    ValidateObjectName(request.QualTable);
                    ValidateObjectName(ResolveSelectedColumn(request.QualColumn));
                    break;
                case 4:
                    ValidateObjectName(request.CrseTable);
                    ValidateObjectName(ResolveSelectedColumn(request.CrseColumn));
                    break;
                case 5:
                    ValidateObjectName(request.StudTable);
                    ValidateObjectName(ResolveSelectedColumn(request.StudColumn));
                    break;
                case 6:
                    ValidateObjectName(request.StudTable);
                    ValidateObjectName(ResolveSelectedColumn(request.StudColumn));
                    break;
                case 7:
                    ValidateObjectName(request.StudTable);
                    ValidateObjectName(request.QualTable);
                    ValidateObjectName(ResolveSelectedColumn(request.StudColumn));
                    ValidateObjectName(ResolveSelectedColumn(request.QualColumn));
                    ValidateObjectName(ResolveSelectedColumn(ParseRuleParameters(request.RuleParameterJson, request.RuleNumber).ContextColumn));
                    break;
                case 8:
                    ValidateObjectName(request.CregTable);
                    ValidateObjectName(request.CrseTable);
                    ValidateObjectName(ResolveSelectedColumn(request.CregColumn));
                    ValidateObjectName(ResolveSelectedColumn(request.CrseColumn));
                    ValidateObjectName(ResolveSelectedColumn(ParseRuleParameters(request.RuleParameterJson, request.RuleNumber).ContextColumn));
                    break;
                case 9:
                    ValidateObjectName(request.CregTable);
                    ValidateObjectName(request.StudTable);
                    ValidateObjectName(ResolveSelectedColumn(request.CregColumn));
                    ValidateObjectName(ResolveSelectedColumn(request.StudColumn));
                    ValidateObjectName(ResolveSelectedColumn(ParseRuleParameters(request.RuleParameterJson, request.RuleNumber).ContextColumn));
                    ValidateObjectName(ResolveSelectedColumn(ParseRuleParameters(request.RuleParameterJson, request.RuleNumber).SecondaryContextColumn));
                    break;
                case 10:
                    foreach (var dataset in ResolveRule10JoinDatasets(request.Rule10JoinConfigJson))
                    {
                        ValidateObjectName(dataset.TableName);
                        foreach (var keyColumn in dataset.KeyColumns)
                            ValidateObjectName(keyColumn);
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Integrity rule {request.RuleNumber} is not supported.");
            }
        }

        private static bool RequiresTable(int ruleNumber, string tableCode) =>
            IntegrityRuleCatalog.Get(ruleNumber).RequiredTables.Contains(tableCode, StringComparer.OrdinalIgnoreCase);

        private static string GetDefaultQualColumn(int ruleNumber) => ruleNumber switch
        {
            1 => "_005",
            2 => "_004",
            3 => "_001",
            7 => "_001",
            _ => "_005"
        };

        private static string GetDefaultStudColumn(int ruleNumber) => ruleNumber switch
        {
            5 => "_007",
            6 => "_106",
            7 => "_001",
            9 => "_007",
            _ => "_007"
        };

        private static string GetDefaultCregColumn(int ruleNumber) => ruleNumber switch
        {
            8 => "_030",
            9 => "_007",
            _ => "_030"
        };

        private static string GetDefaultCrseColumn(int ruleNumber) => "_030";

        private static string GetDefaultRuleParameterJson(int ruleNumber) => ruleNumber switch
        {
            5 => JsonConvert.SerializeObject(new IntegrityRuleParameterSet { MatchValue = "9999999" }),
            7 => JsonConvert.SerializeObject(new IntegrityRuleParameterSet { ContextColumn = "_007" }),
            8 => JsonConvert.SerializeObject(new IntegrityRuleParameterSet { ContextColumn = "_007" }),
            9 => JsonConvert.SerializeObject(new IntegrityRuleParameterSet { ContextColumn = "_001", SecondaryContextColumn = "_001" }),
            _ => ""
        };

        private static string ResolveSelectedColumn(string? selectedColumn, string fallbackColumn = "")
        {
            var resolved = string.IsNullOrWhiteSpace(selectedColumn) ? fallbackColumn : selectedColumn.Trim();
            if (string.IsNullOrWhiteSpace(resolved))
                throw new InvalidOperationException("Table or column name is required.");

            ValidateObjectName(resolved);
            return resolved;
        }

        private static IntegrityRuleParameterSet ParseRuleParameters(string? ruleParameterJson, int ruleNumber)
        {
            var fallback = ruleNumber switch
            {
                5 => new IntegrityRuleParameterSet { MatchValue = "9999999" },
                _ => new IntegrityRuleParameterSet()
            };

            if (string.IsNullOrWhiteSpace(ruleParameterJson))
                return fallback;

            try
            {
                var parsed = JsonConvert.DeserializeObject<IntegrityRuleParameterSet>(ruleParameterJson);
                if (parsed == null)
                    return fallback;

                return new IntegrityRuleParameterSet
                {
                    MatchValue = string.IsNullOrWhiteSpace(parsed.MatchValue) ? fallback.MatchValue : parsed.MatchValue.Trim(),
                    ContextColumn = string.IsNullOrWhiteSpace(parsed.ContextColumn) ? fallback.ContextColumn : parsed.ContextColumn.Trim(),
                    SecondaryContextColumn = string.IsNullOrWhiteSpace(parsed.SecondaryContextColumn) ? fallback.SecondaryContextColumn : parsed.SecondaryContextColumn.Trim()
                };
            }
            catch
            {
                return fallback;
            }
        }

        private static string ResolveRuleParameterValue(string? selectedValue, string fallbackValue)
            => string.IsNullOrWhiteSpace(selectedValue) ? fallbackValue : selectedValue.Trim();

        private static string ToSqlLiteral(string value)
            => $"N'{value.Replace("'", "''")}'";

        private static string EscapeCriteriaText(string value)
            => value.Replace("\"", "\"\"");

        private sealed class IntegrityRuleParameterSet
        {
            public string? MatchValue { get; set; }
            public string? ContextColumn { get; set; }
            public string? SecondaryContextColumn { get; set; }
        }

        private static List<Rule10JoinDatasetConfigItem> ResolveRule10JoinDatasets(string? joinConfigJson)
        {
            if (!string.IsNullOrWhiteSpace(joinConfigJson))
            {
                try
                {
                    var configured = JsonConvert.DeserializeObject<List<Rule10JoinDatasetConfigItem>>(joinConfigJson);
                    if (configured != null && configured.Count > 0)
                    {
                        return configured
                            .Where(item => !string.IsNullOrWhiteSpace(item.TableName))
                            .Select(item => new Rule10JoinDatasetConfigItem
                            {
                                DatasetCode = item.DatasetCode,
                                DatasetLabel = string.IsNullOrWhiteSpace(item.DatasetLabel) ? item.DatasetCode : item.DatasetLabel,
                                TableName = item.TableName.Trim(),
                                KeyColumns = item.KeyColumns.Where(column => !string.IsNullOrWhiteSpace(column)).Select(column => column.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                                CompositeKeyFields = item.CompositeKeyFields.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList()
                            })
                            .ToList();
                    }
                }
                catch
                {
                }
            }

            return Rule10JoinDatasets
                .Select(item => new Rule10JoinDatasetConfigItem
                {
                    DatasetCode = item.DatasetCode,
                    DatasetLabel = item.DatasetLabel,
                    TableName = item.DefaultTableName,
                    KeyColumns = item.KeyColumns.ToList(),
                    CompositeKeyFields = item.CompositeKeyFields.ToList()
                })
                .ToList();
        }

        private string BuildRule10JoinVerificationSql(Rule10ValidationRequest request, IntegrityRuleMetadata rule)
        {
            var lines = new List<string>
            {
                $"-- HEMIS {rule.RuleLabel.ToUpperInvariant()}: {rule.RuleTitle.ToUpperInvariant()}",
                "-- Rule mode: documented join-key existence verification",
                $"-- Database: {request.Database}",
                "-- The SQL below confirms that each selected table contains the documented key columns.",
                ""
            };

            foreach (var dataset in ResolveRule10JoinDatasets(request.Rule10JoinConfigJson))
            {
                var safeTable = Sanitise(dataset.TableName);
                var (tableSchema, objectName) = SplitSchemaAndTableName(dataset.TableName);
                var safeObjectName = Sanitise(objectName);
                var safeSchema = string.IsNullOrWhiteSpace(tableSchema) ? string.Empty : Sanitise(tableSchema);
                var safeColumns = dataset.KeyColumns.Select(column => ResolveSelectedColumn(column)).ToList();
                var inList = string.Join(", ", safeColumns.Select(column => $"'{column}'"));

                lines.Add($"-- {dataset.DatasetLabel}: key fields {string.Join(", ", dataset.KeyColumns.Concat(dataset.CompositeKeyFields))}");
                lines.Add($@"SELECT
    '{dataset.DatasetLabel}' AS DatasetLabel,
    '{dataset.TableName}' AS TableName,
    '{string.Join(", ", dataset.KeyColumns)}' AS RequiredKeyColumns,
    COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ('{safeTable}', '{safeObjectName}')
  AND (
        TABLE_NAME = '{safeTable}'
        OR '{safeSchema}' = ''
        OR TABLE_SCHEMA = '{safeSchema}'
      )
  AND COLUMN_NAME IN ({inList})
ORDER BY COLUMN_NAME;");
                lines.Add("");
            }

            return string.Join(Environment.NewLine, lines).Trim();
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

        private static (string SchemaName, string TableName) SplitSchemaAndTableName(string value)
        {
            var trimmed = value.Trim();
            var parts = trimmed.Split('.', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length == 2
                ? (parts[0], parts[1])
                : ("", trimmed);
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

        private static async Task ExecuteNonQueryAsync(SqlConnection connection, string sql)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        private static Rule10ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                if (string.IsNullOrWhiteSpace(decoded))
                    return null;
                return JsonConvert.DeserializeObject<Rule10ValidationSummary>(decoded);
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

        private static async Task UpdateStoredSummaryAsync(SqlConnection connection, int runId, Rule10ValidationSummary summary)
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

        private static void EnrichRule10DisplayValues(Rule10ValidationRowRecord row)
        {
            row.DisplayValues["RULE_LABEL"] = row.ControlLabel;
            row.DisplayValues["RESULT_BADGE"] = row.ValidationResult;
            row.DisplayValues["FINAL_RESULT_MESSAGE"] = row.ValidationExplanation;
        }

        private static string ReadValue(IReadOnlyDictionary<string, string?> values, string key) =>
            values.TryGetValue(key, out var value) ? value ?? "" : "";

        private sealed record IntegrityRuleDefinition(
            int RuleId,
            string Label,
            string CriteriaText,
            string TableName,
            string Severity,
            string CountSql,
            string TotalSql,
            string ReviewSql,
            string? SampleReviewSql = null,
            string? PrepSql = null);
    }
}
