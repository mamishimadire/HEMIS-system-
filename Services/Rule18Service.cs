using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class Rule18Service : IRule18Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;

        public Rule18Service(IConfiguration configuration)
        {
            _configuration = configuration;
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

        public async Task<Rule18TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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

                return new Rule18TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = FindFirst(tables, ["dbo_STUD", "STUD"], ["stud"]),
                    AutoBridgeTable = FindFirst(tables, ["dbo_CREG", "CREG", "dbo_CRED", "CRED"], ["creg", "cred"]),
                    AutoCrseTable = FindFirst(tables, ["dbo_CRSE", "CRSE"], ["crse"])
                };
            }
            catch (Exception ex)
            {
                return new Rule18TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule18VerifyResult> VerifyTablesAsync(Rule18VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable);

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var studTable = Sanitise(request.StudTable);
                var bridgeTable = Sanitise(request.BridgeTable);
                var crseTable = Sanitise(request.CrseTable);

                var studCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{studTable}];");
                var bridgeCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{bridgeTable}];");
                var crseCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{crseTable}];");

                await using var command = conn.CreateConfiguredCommand();
                command.CommandText = BuildPopulationCountSql(studTable, bridgeTable, crseTable);
                await using var reader = await command.ExecuteReaderAsync();

                var result = new Rule18VerifyResult
                {
                    Success = true,
                    StudRecordCount = studCount,
                    BridgeRecordCount = bridgeCount,
                    CrseRecordCount = crseCount
                };

                if (await reader.ReadAsync())
                {
                    result.NsfasPopulationCount = GetInt(reader, 0);
                    result.Control1PopulationCount = GetInt(reader, 1);
                    result.Control2PopulationCount = GetInt(reader, 2);
                    result.Control3PopulationCount = GetInt(reader, 3);
                }

                return result;
            }
            catch (Exception ex)
            {
                return new Rule18VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule18ValidationSummary> RunValidationAsync(Rule18ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable);

                var summary = await AnalyseAsync(request, includeAllReviewRows: false);
                if (summary.Success && request.ClientId > 0)
                {
                    try
                    {
                        summary.SavedRunId = await SaveValidationRunAsync(request, summary, userEmail, userName);
                    }
                    catch (Exception ex)
                    {
                        summary.Success = false;
                        summary.Error = $"Validation completed, but the saved run could not be written to the system database: {ex.Message}";
                        return summary;
                    }
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule18ValidationSummary
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule18ValidationSummary> GetExportSummaryAsync(Rule18ValidationRequest request)
        {
            ValidateRequest(request);
            await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable);
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule18WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
    ISNULL(vr.StudTable, '') AS StudTable,
    ISNULL(vr.DeceasedTable, '') AS BridgeTable,
    ISNULL(vr.StudColumn, '') AS CrseTable,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 18
  AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var deserializedSummary = DeserializeSummary(reader.IsDBNull(10) ? null : reader.GetString(10));
            if (deserializedSummary != null && includeSummary)
                ApplyBrowserPreview(deserializedSummary);
            var summary = includeSummary ? deserializedSummary : null;

            var workspace = new Rule18WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                StudTable = reader.IsDBNull(4) ? "dbo_STUD" : reader.GetString(4),
                BridgeTable = reader.IsDBNull(5) ? "dbo_CREG" : reader.GetString(5),
                CrseTable = reader.IsDBNull(6) ? "dbo_CRSE" : reader.GetString(6),
                CurrentStatus = reader.IsDBNull(7) ? "" : reader.GetString(7),
                LastEditedByUserName = reader.IsDBNull(8) ? null : reader.GetString(8),
                LastEditedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                Summary = summary
            };

            if (summary != null)
                workspace.CurrentStatus = summary.Status;

            await reader.CloseAsync();

            workspace.Driver = "ODBC Driver 17 for SQL Server";
            workspace.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId!.Value, currentUserId);
            workspace.HasDataAnalystSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            var currentUserSignoff = signoffs.FirstOrDefault(s => s.IsCurrentUser);
            workspace.CurrentUserHasSignedOff = currentUserSignoff != null;
            workspace.CurrentUserSignoffComment = currentUserSignoff?.Comment ?? "";

            if (workspace.Summary != null && workspace.Summary.SavedRunId.GetValueOrDefault() <= 0)
                workspace.Summary.SavedRunId = workspace.RunId;

            return workspace;
        }

        public async Task<Rule18RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 18;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var sourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            var clientId = reader.GetInt32(1);
            summary.ClientId = clientId;
            if (summary.SavedRunId.GetValueOrDefault() <= 0)
                summary.SavedRunId = runId;

            if (includeFullResults)
            {
                summary = await ExpandSavedSummaryIfNeededAsync(summary, sourceServer);
                summary.DisplayedCount = summary.ReviewRows.Count;
                summary.IsPreviewOnly = false;
                summary.PreviewLimit = 0;
            }
            else
            {
                ApplyBrowserPreview(summary);
            }

            var review = new Rule18RunReviewViewModel
            {
                RunId = reader.GetInt32(0),
                ClientId = clientId,
                IsCurrentRun = !reader.IsDBNull(2) && reader.GetBoolean(2),
                EngagementName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                MaconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SourceServer = sourceServer,
                Summary = summary
            };

            await reader.CloseAsync();

            review.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";
            review.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            review.HasDataAnalystSignoff = review.Signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            return review;
        }

        public async Task<Rule18WorkspaceSaveResult> SaveWorkspaceAsync(Rule18ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                ValidateRequest(request);

                if (request.RunId is null || request.RunId <= 0)
                {
                    return new Rule18WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run the validation first so the workspace can be saved."
                    };
                }

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                {
                    return new Rule18WorkspaceSaveResult
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
SET HemisServer = @HemisServer,
    AuditDatabase = @AuditDatabase,
    StudTable = @StudTable,
    DeceasedTable = @BridgeTable,
    StudColumn = @CrseTable,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID
  AND ClientID = @ClientID
  AND RuleNumber = 18;";
                command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                command.Parameters.AddWithValue("@ClientID", request.ClientId);
                command.Parameters.AddWithValue("@HemisServer", request.Server);
                command.Parameters.AddWithValue("@AuditDatabase", request.Database);
                command.Parameters.AddWithValue("@StudTable", request.StudTable);
                command.Parameters.AddWithValue("@BridgeTable", request.BridgeTable);
                command.Parameters.AddWithValue("@CrseTable", request.CrseTable);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)(reviewerName ?? reviewerEmail) ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule18|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|{request.StudTable}|{request.BridgeTable}|{request.CrseTable}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule18WorkspaceSaveResult
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
                return new Rule18WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule18WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule18WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Saved workspace was not found."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash = await GetValidationRecordHashAsync(connection, runId);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID;";
                command.Parameters.AddWithValue("@RunID", runId);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)(reviewerName ?? reviewerEmail) ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule18|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule18WorkspaceSaveResult
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
                return new Rule18WorkspaceSaveResult
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
                throw new InvalidOperationException("The selected Rule 18 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var signoffRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(signoffRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can sign off a Rule 18 run.");

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
                throw new InvalidOperationException("The selected Rule 18 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var removal = await ReviewSignoffSqlHelper.RemoveReviewerSignoffWithVersioningAsync(connection, runId, reviewerId.Value);
            if (removal.RemovedCount <= 0)
                return;
        }

        public Task<string> GenerateSqlAsync(Rule18ValidationRequest request)
        {
            ValidateRequest(request);

            var studTable = Sanitise(request.StudTable);
            var bridgeTable = Sanitise(request.BridgeTable);
            var crseTable = Sanitise(request.CrseTable);

            var sql = $@"-- CONTROL 1: NSFAS + Foundation
SELECT 
    'Control_1' AS Control_Type,
    S._007, S._001, S._019, S._024,
    BRIDGE._030, CRSE._091,
    CASE 
        WHEN ISNULL(S._019, '') = 'NS' AND ISNULL(CRSE._091, '') = 'Y'
        THEN 'PASS' ELSE 'FAIL'
    END AS Control_Check
FROM [{studTable}] S
INNER JOIN [{bridgeTable}] BRIDGE ON S._007 = BRIDGE._007
INNER JOIN [{crseTable}] CRSE ON BRIDGE._030 = CRSE._030
WHERE ISNULL(S._019, '') = 'NS' AND ISNULL(CRSE._091, '') = 'Y'
ORDER BY NEWID();

-- CONTROL 2: NSFAS + Foundation + Distance
SELECT 
    'Control_2' AS Control_Type,
    S._007, S._001, S._019, S._024,
    BRIDGE._030, CRSE._091,
    CASE 
        WHEN ISNULL(S._019, '') = 'NS' 
         AND ISNULL(CRSE._091, '') = 'Y'
         AND ISNULL(S._024, '') = 'D'
        THEN 'PASS' ELSE 'FAIL'
    END AS Control_Check
FROM [{studTable}] S
INNER JOIN [{bridgeTable}] BRIDGE ON S._007 = BRIDGE._007
INNER JOIN [{crseTable}] CRSE ON BRIDGE._030 = CRSE._030
WHERE ISNULL(S._019, '') = 'NS'
  AND ISNULL(CRSE._091, '') = 'Y'
  AND ISNULL(S._024, '') = 'D'
ORDER BY NEWID();

-- CONTROL 3: NSFAS only
SELECT 
    'Control_3' AS Control_Type,
    S._007, S._001, S._019, S._024,
    BRIDGE._030, CRSE._091,
    CASE 
        WHEN ISNULL(S._019, '') = 'NS' 
         AND ISNULL(CRSE._091, '') <> 'Y'
         AND ISNULL(S._024, '') <> 'D'
        THEN 'PASS' ELSE 'FAIL'
    END AS Control_Check
FROM [{studTable}] S
INNER JOIN [{bridgeTable}] BRIDGE ON S._007 = BRIDGE._007
INNER JOIN [{crseTable}] CRSE ON BRIDGE._030 = CRSE._030
WHERE ISNULL(S._019, '') = 'NS'
  AND ISNULL(CRSE._091, '') <> 'Y'
  AND ISNULL(S._024, '') <> 'D'
ORDER BY NEWID();";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule18ValidationSummary> AnalyseAsync(Rule18ValidationRequest request, bool includeAllReviewRows)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var studTable = Sanitise(request.StudTable);
            var bridgeTable = Sanitise(request.BridgeTable);
            var crseTable = Sanitise(request.CrseTable);

            var studCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{studTable}];");
            var bridgeCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{bridgeTable}];");
            var crseCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{crseTable}];");

            await using var countCommand = conn.CreateConfiguredCommand();
            countCommand.CommandText = BuildPopulationCountSql(studTable, bridgeTable, crseTable);
            await using var countReader = await countCommand.ExecuteReaderAsync();

            var nsfasPopulationCount = 0;
            var control1PassPopulation = 0;
            var control2PassPopulation = 0;
            var control3PassPopulation = 0;
            if (await countReader.ReadAsync())
            {
                nsfasPopulationCount = GetInt(countReader, 0);
                control1PassPopulation = GetInt(countReader, 1);
                control2PassPopulation = GetInt(countReader, 2);
                control3PassPopulation = GetInt(countReader, 3);
            }

            await countReader.CloseAsync();

            var reviewRows = await LoadControlRowsAsync(conn, studTable, bridgeTable, crseTable, includeAllReviewRows ? null : BrowserPreviewRowLimit);
            reviewRows = NormalizeReviewRows(reviewRows);

            var controlSummaries = BuildControlSummaries(control1PassPopulation, control2PassPopulation, control3PassPopulation);
            var totalValidated = controlSummaries.Sum(x => x.TotalCount);
            var passCount = controlSummaries.Sum(x => x.PassCount);
            var failCount = controlSummaries.Sum(x => x.FailCount);
            var isPreviewOnly = !includeAllReviewRows && totalValidated > reviewRows.Count;

            return new Rule18ValidationSummary
            {
                Success = true,
                StudRecordCount = studCount,
                BridgeRecordCount = bridgeCount,
                CrseRecordCount = crseCount,
                NsfasPopulationCount = nsfasPopulationCount,
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
                StudTable = request.StudTable,
                BridgeTable = request.BridgeTable,
                CrseTable = request.CrseTable,
                TableLinkageText = $"{request.StudTable} -> {request.BridgeTable} -> {request.CrseTable}",
                RuleModeText = "100% population testing of matching control rows",
                ProcedureSteps = BuildProcedureSteps(request.StudTable, request.BridgeTable, request.CrseTable),
                ClientId = request.ClientId,
                ControlSummaries = controlSummaries,
                ReviewRows = reviewRows,
                Warning = includeAllReviewRows
                    ? "Rule 18 completed with the full matching control result set."
                    : "Counts reflect the full matching control result set. Browser review rows are limited for performance."
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule18ValidationRequest request, Rule18ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 18);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 18);
            var failRows = summary.ReviewRows.Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).ToList();

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
    @ClientID, @UserID, 18, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @BridgeTable, @CrseTable, NULL,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "NSFAS Student Validation");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.StudTable);
            command.Parameters.AddWithValue("@BridgeTable", request.BridgeTable);
            command.Parameters.AddWithValue("@CrseTable", request.CrseTable);
            var persistedSummary = CloneSummary(summary);
            persistedSummary.SavedRunId = summary.SavedRunId;
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(failRows)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persistedSummary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCommand = connection.CreateConfiguredCommand();
            hashCommand.CommandText = @"
UPDATE dbo.ValidationRuns
SET RecordHash = @RecordHash
WHERE RunID = @RunID;";
            hashCommand.Parameters.AddWithValue("@RunID", runId);
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule18|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
        }

        private static string BuildPopulationCountSql(string studTable, string bridgeTable, string crseTable) => $@"
{BuildRule18SourceCtes(studTable, bridgeTable, crseTable)}
SELECT
    (SELECT COUNT(*) FROM NsfasStudents) AS NsfasPopulationCount,
    (SELECT COUNT(*) FROM Control1PassRows) AS Control1PassPopulation,
    (SELECT COUNT(*) FROM Control2PassRows) AS Control2PassPopulation,
    (SELECT COUNT(*) FROM Control3PassRows) AS Control3PassPopulation;";

        private async Task<List<Rule18ValidationRowRecord>> LoadControlRowsAsync(SqlConnection connection, string studTable, string bridgeTable, string crseTable, int? maxRows)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = BuildAllControlsSql(studTable, bridgeTable, crseTable, maxRows);

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule18ValidationRowRecord>();
            while (await reader.ReadAsync())
            {
                var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    displayValues[reader.GetName(i)] = reader.IsDBNull(i)
                        ? null
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                }

                rows.Add(new Rule18ValidationRowRecord
                {
                    ValidationNumber = rows.Count + 1,
                    ControlType = ReadValue(displayValues, "Control_Type"),
                    ControlLabel = ReadValue(displayValues, "Control_Label"),
                    ValidationResult = ReadValue(displayValues, "Validation_Result"),
                    ValidationExplanation = ReadValue(displayValues, "Validation_Explanation"),
                    DisplayValues = displayValues
                });

                EnrichRule18DisplayValues(rows[^1]);
            }

            return rows;
        }

        private static string BuildAllControlsSql(string studTable, string bridgeTable, string crseTable, int? maxRows)
        {
            var perControlLimit = maxRows.HasValue && maxRows.Value > 0
                ? Math.Max(maxRows.Value / 3, 1)
                : 0;

            return $@"
WITH ControlResults AS
(
    SELECT
        1 AS Control_Sort,
        'Control_1' AS Control_Type,
        'CONTROL 1: _019=''NS'' AND _091=''Y''' AS Control_Label,
        CASE
            WHEN ISNULL(CAST(S.[_019] AS nvarchar(255)), '') = 'NS'
             AND ISNULL(CAST(CRSE.[_091] AS nvarchar(255)), '') = 'Y'
            THEN 'PASS' ELSE 'FAIL'
        END AS Validation_Result,
        'Matched Rule 18 Control 1.' AS Validation_Explanation,
        CAST(S.[_001] AS nvarchar(255)) AS STUD__001,
        CAST(S.[_007] AS nvarchar(255)) AS STUD__007,
        CAST(S.[_019] AS nvarchar(255)) AS STUD__019,
        CAST(S.[_024] AS nvarchar(255)) AS STUD__024,
        CAST(BRIDGE.[_001] AS nvarchar(255)) AS BRIDGE__001,
        CAST(BRIDGE.[_030] AS nvarchar(255)) AS BRIDGE__030,
        CAST(CRSE.[_030] AS nvarchar(255)) AS CRSE__030,
        CAST(CRSE.[_091] AS nvarchar(255)) AS CRSE__091
    FROM [{studTable}] S
    INNER JOIN [{bridgeTable}] BRIDGE ON S.[_007] = BRIDGE.[_007]
    INNER JOIN [{crseTable}] CRSE ON BRIDGE.[_030] = CRSE.[_030]
    WHERE ISNULL(CAST(S.[_019] AS nvarchar(255)), '') = 'NS'
      AND ISNULL(CAST(CRSE.[_091] AS nvarchar(255)), '') = 'Y'

    UNION ALL

    SELECT
        2 AS Control_Sort,
        'Control_2' AS Control_Type,
        'CONTROL 2: _019=''NS'' AND _091=''Y'' AND _024=''D''' AS Control_Label,
        CASE
            WHEN ISNULL(CAST(S.[_019] AS nvarchar(255)), '') = 'NS'
             AND ISNULL(CAST(CRSE.[_091] AS nvarchar(255)), '') = 'Y'
             AND ISNULL(CAST(S.[_024] AS nvarchar(255)), '') = 'D'
            THEN 'PASS' ELSE 'FAIL'
        END AS Validation_Result,
        'Matched Rule 18 Control 2.' AS Validation_Explanation,
        CAST(S.[_001] AS nvarchar(255)) AS STUD__001,
        CAST(S.[_007] AS nvarchar(255)) AS STUD__007,
        CAST(S.[_019] AS nvarchar(255)) AS STUD__019,
        CAST(S.[_024] AS nvarchar(255)) AS STUD__024,
        CAST(BRIDGE.[_001] AS nvarchar(255)) AS BRIDGE__001,
        CAST(BRIDGE.[_030] AS nvarchar(255)) AS BRIDGE__030,
        CAST(CRSE.[_030] AS nvarchar(255)) AS CRSE__030,
        CAST(CRSE.[_091] AS nvarchar(255)) AS CRSE__091
    FROM [{studTable}] S
    INNER JOIN [{bridgeTable}] BRIDGE ON S.[_007] = BRIDGE.[_007]
    INNER JOIN [{crseTable}] CRSE ON BRIDGE.[_030] = CRSE.[_030]
    WHERE ISNULL(CAST(S.[_019] AS nvarchar(255)), '') = 'NS'
      AND ISNULL(CAST(CRSE.[_091] AS nvarchar(255)), '') = 'Y'
      AND ISNULL(CAST(S.[_024] AS nvarchar(255)), '') = 'D'

    UNION ALL

    SELECT
        3 AS Control_Sort,
        'Control_3' AS Control_Type,
        'CONTROL 3: _019=''NS'' AND _091<>''Y'' AND _024<>''D''' AS Control_Label,
        CASE
            WHEN ISNULL(CAST(S.[_019] AS nvarchar(255)), '') = 'NS'
             AND ISNULL(CAST(CRSE.[_091] AS nvarchar(255)), '') <> 'Y'
             AND ISNULL(CAST(S.[_024] AS nvarchar(255)), '') <> 'D'
            THEN 'PASS' ELSE 'FAIL'
        END AS Validation_Result,
        'Matched Rule 18 Control 3.' AS Validation_Explanation,
        CAST(S.[_001] AS nvarchar(255)) AS STUD__001,
        CAST(S.[_007] AS nvarchar(255)) AS STUD__007,
        CAST(S.[_019] AS nvarchar(255)) AS STUD__019,
        CAST(S.[_024] AS nvarchar(255)) AS STUD__024,
        CAST(BRIDGE.[_001] AS nvarchar(255)) AS BRIDGE__001,
        CAST(BRIDGE.[_030] AS nvarchar(255)) AS BRIDGE__030,
        CAST(CRSE.[_030] AS nvarchar(255)) AS CRSE__030,
        CAST(CRSE.[_091] AS nvarchar(255)) AS CRSE__091
    FROM [{studTable}] S
    INNER JOIN [{bridgeTable}] BRIDGE ON S.[_007] = BRIDGE.[_007]
    INNER JOIN [{crseTable}] CRSE ON BRIDGE.[_030] = CRSE.[_030]
    WHERE ISNULL(CAST(S.[_019] AS nvarchar(255)), '') = 'NS'
      AND ISNULL(CAST(CRSE.[_091] AS nvarchar(255)), '') <> 'Y'
      AND ISNULL(CAST(S.[_024] AS nvarchar(255)), '') <> 'D'
)
SELECT *
FROM
(
    SELECT
        ControlResults.*,
        ROW_NUMBER() OVER
        (
            PARTITION BY Control_Type
            ORDER BY NEWID()
        ) AS Preview_Row_Num
    FROM ControlResults
) Results
WHERE {(
                perControlLimit > 0
                    ? $"Preview_Row_Num <= {perControlLimit}"
                    : "1 = 1"
            )}
ORDER BY Control_Sort, Preview_Row_Num;";
        }

        private static string BuildRule18SourceCtes(string studTable, string bridgeTable, string crseTable) => $@"
WITH BaseRows AS
(
    SELECT
        CAST(S.[_001] AS nvarchar(255)) AS Stud_001,
        CAST(S.[_007] AS nvarchar(255)) AS Stud_007,
        CAST(S.[_019] AS nvarchar(255)) AS Stud_019,
        CAST(S.[_024] AS nvarchar(255)) AS Stud_024,
        CAST(BRIDGE.[_001] AS nvarchar(255)) AS Bridge_001,
        CAST(BRIDGE.[_030] AS nvarchar(255)) AS Bridge_030,
        CAST(CRSE.[_030] AS nvarchar(255)) AS Crse_030,
        CAST(CRSE.[_091] AS nvarchar(255)) AS Crse_091,
        CASE WHEN ISNULL(CAST(S.[_019] AS nvarchar(255)), '') = 'NS' THEN 1 ELSE 0 END AS IsNsfasStudent,
        CASE WHEN ISNULL(CAST(S.[_024] AS nvarchar(255)), '') = 'D' THEN 1 ELSE 0 END AS IsDistanceStudent,
        CASE WHEN ISNULL(CAST(CRSE.[_091] AS nvarchar(255)), '') = 'Y' THEN 1 ELSE 0 END AS IsFoundationCourse
    FROM [{studTable}] S
    INNER JOIN [{bridgeTable}] BRIDGE ON S.[_007] = BRIDGE.[_007]
    INNER JOIN [{crseTable}] CRSE ON BRIDGE.[_030] = CRSE.[_030]
),
NsfasStudents AS
(
    SELECT DISTINCT
        Stud_001,
        Stud_007,
        Stud_019,
        Stud_024
    FROM BaseRows
    WHERE IsNsfasStudent = 1
),
Control1PassRows AS
(
    SELECT
        B.*
    FROM BaseRows B
    WHERE B.IsNsfasStudent = 1
      AND B.IsFoundationCourse = 1
),
Control2PassRows AS
(
    SELECT
        B.*
    FROM BaseRows B
    WHERE B.IsNsfasStudent = 1
      AND B.IsFoundationCourse = 1
      AND B.IsDistanceStudent = 1
),
Control3PassRows AS
(
    SELECT
        B.*
    FROM BaseRows B
    WHERE B.IsNsfasStudent = 1
      AND B.IsFoundationCourse = 0
      AND B.IsDistanceStudent = 0
)";

        private static List<Rule18ControlSummaryItemViewModel> BuildControlSummaries(
            int control1PassPopulation,
            int control2PassPopulation,
            int control3PassPopulation)
        {
            return new List<Rule18ControlSummaryItemViewModel>
            {
                BuildControlSummary("Control_1", "Control 1", "_019='NS' AND _091='Y'", control1PassPopulation),
                BuildControlSummary("Control_2", "Control 2", "_019='NS' AND _091='Y' AND _024='D'", control2PassPopulation),
                BuildControlSummary("Control_3", "Control 3", "_019='NS' AND _091<>'Y' AND _024<>'D'", control3PassPopulation)
            };
        }

        private static Rule18ControlSummaryItemViewModel BuildControlSummary(
            string controlType,
            string controlLabel,
            string criteriaText,
            int passCount)
        {
            return new Rule18ControlSummaryItemViewModel
            {
                ControlType = controlType,
                ControlLabel = controlLabel,
                CriteriaText = criteriaText,
                RequestedCount = passCount,
                AvailableCount = passCount,
                AchievedCount = passCount,
                TotalCount = passCount,
                PassCount = passCount,
                FailCount = 0,
                Status = passCount > 0 ? "PASS" : "NO DATA"
            };
        }

        private static List<Rule18ValidationRowRecord> NormalizeReviewRows(IEnumerable<Rule18ValidationRowRecord>? rows)
        {
            var normalized = (rows ?? Enumerable.Empty<Rule18ValidationRowRecord>())
                .Select((row, index) =>
                {
                    row.ValidationNumber = index + 1;
                    return row;
                })
                .ToList();

            return normalized;
        }

        private async Task<Rule18ValidationSummary> ExpandSavedSummaryIfNeededAsync(Rule18ValidationSummary summary, string? server)
        {
            var looksLikeStoredPreviewSample =
                summary.ReviewRows.Count > 0 &&
                summary.ReviewRows.Count <= BrowserPreviewRowLimit &&
                summary.TotalValidated > 0;

            if (!summary.IsPreviewOnly &&
                summary.ReviewRows.Count >= summary.TotalValidated &&
                !looksLikeStoredPreviewSample)
            {
                return summary;
            }

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(summary.Database) ||
                string.IsNullOrWhiteSpace(summary.StudTable) ||
                string.IsNullOrWhiteSpace(summary.BridgeTable) ||
                string.IsNullOrWhiteSpace(summary.CrseTable))
            {
                return summary;
            }

            try
            {
                var expanded = await AnalyseAsync(
                    new Rule18ValidationRequest
                    {
                        ClientId = summary.ClientId,
                        RunId = summary.SavedRunId,
                        Server = server,
                        Database = summary.Database,
                        Driver = "ODBC Driver 17 for SQL Server",
                        StudTable = summary.StudTable,
                        BridgeTable = summary.BridgeTable,
                        CrseTable = summary.CrseTable
                    },
                    includeAllReviewRows: true);

                expanded.Timestamp = string.IsNullOrWhiteSpace(summary.Timestamp) ? expanded.Timestamp : summary.Timestamp;
                expanded.ClientId = summary.ClientId;
                expanded.SavedRunId = summary.SavedRunId;
                expanded.Warning = string.IsNullOrWhiteSpace(summary.Warning)
                    ? "Saved Rule 18 results were expanded from the stored browser preview to the full result set."
                    : $"{summary.Warning} Full saved results were reloaded from the saved Rule 18 configuration.";

                return expanded;
            }
            catch
            {
                return summary;
            }
        }

        private static Rule18ValidationSummary CloneSummary(Rule18ValidationSummary summary)
        {
            return new Rule18ValidationSummary
            {
                Success = summary.Success,
                StudRecordCount = summary.StudRecordCount,
                BridgeRecordCount = summary.BridgeRecordCount,
                CrseRecordCount = summary.CrseRecordCount,
                NsfasPopulationCount = summary.NsfasPopulationCount,
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
                StudTable = summary.StudTable,
                BridgeTable = summary.BridgeTable,
                CrseTable = summary.CrseTable,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText = summary.RuleModeText,
                ProcedureSteps = summary.ProcedureSteps.ToList(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries
                    .Select(item => new Rule18ControlSummaryItemViewModel
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

        private static Rule18ValidationRowRecord CloneReviewRow(Rule18ValidationRowRecord row)
        {
            return new Rule18ValidationRowRecord
            {
                ValidationNumber = row.ValidationNumber,
                ControlType = row.ControlType,
                ControlLabel = row.ControlLabel,
                ValidationResult = row.ValidationResult,
                ValidationExplanation = row.ValidationExplanation,
                DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static Rule18ValidationSummary CreateBrowserPreview(Rule18ValidationSummary summary)
        {
            var perControlLimit = Math.Max(BrowserPreviewRowLimit / 3, 1);
            var previewRows = summary.ReviewRows
                .GroupBy(row => row.ControlType, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => GetControlSort(group.Key))
                .SelectMany(group => group.OrderBy(row => row.ValidationNumber).Take(perControlLimit))
                .Take(BrowserPreviewRowLimit)
                .ToList();

            return new Rule18ValidationSummary
            {
                Success = summary.Success,
                StudRecordCount = summary.StudRecordCount,
                BridgeRecordCount = summary.BridgeRecordCount,
                CrseRecordCount = summary.CrseRecordCount,
                NsfasPopulationCount = summary.NsfasPopulationCount,
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
                StudTable = summary.StudTable,
                BridgeTable = summary.BridgeTable,
                CrseTable = summary.CrseTable,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText = summary.RuleModeText,
                ProcedureSteps = summary.ProcedureSteps.ToList(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries
                    .Select(item => new Rule18ControlSummaryItemViewModel
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

        private static void ApplyBrowserPreview(Rule18ValidationSummary summary)
        {
            var preview = CreateBrowserPreview(summary);
            summary.DisplayedCount = preview.DisplayedCount;
            summary.IsPreviewOnly = preview.IsPreviewOnly;
            summary.PreviewLimit = preview.PreviewLimit;
            summary.ReviewRows = preview.ReviewRows;
        }

        private static int GetControlSort(string? controlType) => controlType switch
        {
            "Control_1" => 1,
            "Control_2" => 2,
            "Control_3" => 3,
            _ => 99
        };

        private static List<string> BuildProcedureSteps(string studTable, string bridgeTable, string crseTable) =>
            new()
            {
                $"Link {studTable}._007 to {bridgeTable}._007.",
                $"Link {bridgeTable}._030 to {crseTable}._030.",
                "Evaluate the joined STUD, bridge, and CRSE rows using the same control logic as the notebook.",
                "Count full matching joined rows per control using the exact control-specific bridge and CRSE conditions.",
                "Return the full matching control result set for Control 1, Control 2, and Control 3."
            };

        private async Task EnsureColumnsExistAsync(string server, string database, string driver, string studTable, string bridgeTable, string crseTable)
        {
            var studColumns = await GetTableColumnsAsync(server, database, driver, studTable);
            var bridgeColumns = await GetTableColumnsAsync(server, database, driver, bridgeTable);
            var crseColumns = await GetTableColumnsAsync(server, database, driver, crseTable);

            EnsureHasColumns(studTable, studColumns, "_001", "_007", "_019", "_024");
            EnsureHasColumns(bridgeTable, bridgeColumns, "_001", "_007", "_030");
            EnsureHasColumns(crseTable, crseColumns, "_030", "_091");
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

        private static void ValidateRequest(Rule18ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.StudTable))
                throw new InvalidOperationException("STUD table is required.");
            if (string.IsNullOrWhiteSpace(request.BridgeTable))
                throw new InvalidOperationException("Bridge table is required.");
            if (string.IsNullOrWhiteSpace(request.CrseTable))
                throw new InvalidOperationException("CRSE table is required.");

            ValidateObjectName(request.StudTable);
            ValidateObjectName(request.BridgeTable);
            ValidateObjectName(request.CrseTable);
        }

        private static void ValidateRequest(Rule18VerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.StudTable))
                throw new InvalidOperationException("STUD table is required.");
            if (string.IsNullOrWhiteSpace(request.BridgeTable))
                throw new InvalidOperationException("Bridge table is required.");
            if (string.IsNullOrWhiteSpace(request.CrseTable))
                throw new InvalidOperationException("CRSE table is required.");

            ValidateObjectName(request.StudTable);
            ValidateObjectName(request.BridgeTable);
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

        private static Rule18ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule18ValidationSummary>(decoded);
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

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);

        private static void EnrichRule18DisplayValues(Rule18ValidationRowRecord row)
        {
            var values = row.DisplayValues;
            var controlType = ReadValue(values, "Control_Type");
            var validationResult = ReadValue(values, "Validation_Result");
            var stud024 = ReadValue(values, "STUD__024");
            var crse091 = ReadValue(values, "CRSE__091");

            var isPass = string.Equals(validationResult, "PASS", StringComparison.OrdinalIgnoreCase);
            var isDistanceStudent = string.Equals(stud024.Trim(), "D", StringComparison.OrdinalIgnoreCase);
            var isFoundationCourse = string.Equals(crse091.Trim(), "Y", StringComparison.OrdinalIgnoreCase);
            var stud024Display = FormatRule18ColumnValue(stud024);
            var crse091Display = FormatRule18ColumnValue(crse091);

            string criteriaText;
            string validationExplanation;
            string studCriteriaMessage;
            string bridgeLinkMessage;
            string finalResultMessage;

            switch (controlType)
            {
                case "Control_1":
                    criteriaText = "_019='NS' AND _091='Y'";
                    validationExplanation = isPass
                        ? $"Passed because STUD._019='NS' and the linked CRSE._091='{crse091Display}'."
                        : $"Failed because STUD._019='NS' but the linked CRSE._091='{crse091Display}' instead of 'Y'.";
                    studCriteriaMessage = "Passed: dbo_STUD filtering matched because _019='NS'.";
                    bridgeLinkMessage = isPass
                        ? "Passed: correctly linked through the bridge table to the qualifying CRSE row."
                        : "Passed bridge linkage, but the linked CRSE row did not satisfy this control.";
                    finalResultMessage = isPass
                        ? $"Passed this criteria because CRSE._091='{crse091Display}'."
                        : $"Failed this criteria because CRSE._091='{crse091Display}' instead of 'Y'.";
                    break;

                case "Control_2":
                    criteriaText = "_019='NS' AND _091='Y' AND _024='D'";
                    validationExplanation = isPass
                        ? $"Passed because STUD._024='{stud024Display}' and the linked CRSE._091='{crse091Display}'."
                        : $"Failed because STUD._024='{stud024Display}' instead of 'D'.";
                    studCriteriaMessage = isPass
                        ? $"Passed: dbo_STUD filtering matched because _019='NS' and _024='{stud024Display}'."
                        : $"Failed: dbo_STUD filtering matched NSFAS, but _024='{stud024Display}' instead of 'D'.";
                    bridgeLinkMessage = isPass
                        ? "Passed: correctly linked through the bridge table to the qualifying CRSE row."
                        : "Passed bridge linkage to a foundation CRSE row, but the distance requirement was not met on dbo_STUD.";
                    finalResultMessage = isPass
                        ? $"Passed this criteria because _024='{stud024Display}' and CRSE._091='{crse091Display}'."
                        : $"Failed this criteria because _024='{stud024Display}' instead of 'D'.";
                    break;

                case "Control_3":
                    criteriaText = "_019='NS' AND _091<>'Y' AND _024<>'D'";
                    validationExplanation = isPass
                        ? $"Passed because STUD._024='{stud024Display}' and the linked CRSE._091='{crse091Display}', so both values are outside the excluded set."
                        : BuildControl3FailureReason(stud024Display, crse091Display, isDistanceStudent, isFoundationCourse);
                    studCriteriaMessage = isDistanceStudent
                        ? $"Failed: dbo_STUD filtering matched NSFAS, but _024='{stud024Display}' instead of a non-'D' value."
                        : $"Passed: dbo_STUD filtering matched because _019='NS' and _024='{stud024Display}' is not 'D'.";
                    bridgeLinkMessage = isPass
                        ? "Passed: correctly linked through the bridge table to the qualifying CRSE row."
                        : isFoundationCourse
                            ? "Passed bridge linkage, but the linked CRSE row is still marked as foundation."
                            : "Passed bridge linkage, but the row failed the non-distance requirement on dbo_STUD.";
                    finalResultMessage = isPass
                        ? $"Passed this criteria because _024='{stud024Display}' and CRSE._091='{crse091Display}'."
                        : BuildControl3FinalResultMessage(stud024Display, crse091Display, isDistanceStudent, isFoundationCourse);
                    break;

                default:
                    criteriaText = ReadValue(values, "FINAL_RULE_TEXT");
                    validationExplanation = row.ValidationExplanation;
                    studCriteriaMessage = ReadValue(values, "STUD_CRITERIA_MESSAGE");
                    bridgeLinkMessage = ReadValue(values, "BRIDGE_LINK_MESSAGE");
                    finalResultMessage = ReadValue(values, "FINAL_RESULT_MESSAGE");
                    break;
            }

            values["FINAL_RULE_TEXT"] = criteriaText;
            values["Validation_Explanation"] = validationExplanation;
            values["STUD_CRITERIA_MESSAGE"] = studCriteriaMessage;
            values["BRIDGE_LINK_MESSAGE"] = bridgeLinkMessage;
            values["FINAL_RESULT_MESSAGE"] = finalResultMessage;
            row.ValidationExplanation = validationExplanation;
        }

        private static string BuildControl3FailureReason(string stud024Display, string crse091Display, bool isDistanceStudent, bool isFoundationCourse)
        {
            if (isDistanceStudent && isFoundationCourse)
                return $"Failed because STUD._024='{stud024Display}' and CRSE._091='{crse091Display}', so the row is both distance and foundation.";

            if (isFoundationCourse)
                return $"Failed because the linked CRSE._091='{crse091Display}', which should not be 'Y' for Control 3.";

            if (isDistanceStudent)
                return $"Failed because STUD._024='{stud024Display}', which should not be 'D' for Control 3.";

            return $"Failed because the linked values STUD._024='{stud024Display}' and CRSE._091='{crse091Display}' did not satisfy Control 3.";
        }

        private static string BuildControl3FinalResultMessage(string stud024Display, string crse091Display, bool isDistanceStudent, bool isFoundationCourse)
        {
            if (isDistanceStudent && isFoundationCourse)
                return $"Failed this criteria because _024='{stud024Display}' and _091='{crse091Display}'.";

            if (isFoundationCourse)
                return $"Failed this criteria because _091='{crse091Display}' instead of a non-'Y' value.";

            if (isDistanceStudent)
                return $"Failed this criteria because _024='{stud024Display}' instead of a non-'D' value.";

            return $"Failed this criteria because _024='{stud024Display}' and _091='{crse091Display}' did not satisfy Control 3.";
        }

        private static string FormatRule18ColumnValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "[blank]" : value.Trim();

        private static int GetInt(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

        private static string ReadValue(IReadOnlyDictionary<string, string?> values, string key) =>
            values.TryGetValue(key, out var value) ? value ?? "" : "";
    }
}
