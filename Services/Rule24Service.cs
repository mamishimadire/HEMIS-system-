using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Security.Cryptography;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule24Service : IRule24Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private const int PassSampleLimit = 100;
        private readonly IConfiguration _configuration;

        public Rule24Service(IConfiguration configuration)
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

        public async Task<Rule24TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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

                return new Rule24TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoQualTable = FindFirst(tables,
                        ["dbo_QUAL", "QUAL", "dbo_QUAL_VALIDATION_DETAIL"],
                        ["qual"]),
                    AutoAuditTable = FindFirst(tables,
                        ["MT-audit-prod-QUAL", "MT_AUDIT_PROD_QUAL"],
                        ["audit", "qual"]),
                    AutoH16Table = FindFirst(tables,
                        ["H16QUAL", "H16QUA"],
                        ["h16", "qual"])
                };
            }
            catch (Exception ex)
            {
                return new Rule24TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule24AuditColumnResult> GetAuditColumnsAsync(string server, string database, string driver, string auditTable)
        {
            try
            {
                var columns = await GetTableColumnsAsync(server, database, driver, auditTable);
                return new Rule24AuditColumnResult
                {
                    Success = true,
                    Columns = columns,
                    AutoQualCodeColumn = FindFirst(columns,
                        ["IAIQUAL", "QUALCODE", "_001"],
                        ["iaiqual", "qualcode", "qual"])
                };
            }
            catch (Exception ex)
            {
                return new Rule24AuditColumnResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule24VerifyResult> VerifyTablesAsync(Rule24VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                EnsureAuditTableLooksCorrect(request.AuditTable);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request);

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var qualTable = Sanitise(request.QualTable);
                var auditTable = Sanitise(request.AuditTable);
                var h16Table = Sanitise(request.H16Table);

                return new Rule24VerifyResult
                {
                    Success = true,
                    QualCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{qualTable}];"),
                    AuditCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{auditTable}];"),
                    H16Count = await CountAsync(conn, $"SELECT COUNT(*) FROM [{h16Table}];")
                };
            }
            catch (Exception ex)
            {
                return new Rule24VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule24ValidationSummary> RunValidationAsync(Rule24ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request);

                var summary = await AnalyseAsync(request);
                if (summary.Success && request.ClientId > 0)
                {
                    try
                    {
                        summary.SavedRunId = await SaveValidationRunAsync(request, summary, userEmail, userName);
                    }
                    catch (Exception ex)
                    {
                        summary.Warning = $"Analysis completed, but the saved run could not be persisted automatically: {ex.Message}";
                    }
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule24ValidationSummary
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule24WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
    ISNULL(vr.DeceasedTable, '') AS AuditTable,
    ISNULL(vr.StudColumn, '') AS H16Table,
    ISNULL(vr.DeceasedColumn, '') AS AuditQualCodeColumn,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 24
  AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var deserializedSummary = DeserializeSummary(reader.IsDBNull(11) ? null : reader.GetString(11));
            if (includeSummary && deserializedSummary != null)
            {
                ApplyBrowserPreview(deserializedSummary);
            }
            var summary = includeSummary ? deserializedSummary : null;

            var workspace = new Rule24WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                QualTable = reader.IsDBNull(4) ? "" : reader.GetString(4),
                AuditTable = reader.IsDBNull(5) ? "" : reader.GetString(5),
                H16Table = reader.IsDBNull(6) ? "" : reader.GetString(6),
                AuditQualCodeColumn = reader.IsDBNull(7) ? "IAIQUAL" : reader.GetString(7),
                CurrentStatus = reader.IsDBNull(8) ? "" : reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9) ? null : reader.GetString(9),
                LastEditedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Summary = summary
            };

            if (deserializedSummary != null)
            {
                workspace.QualCodeColumn = deserializedSummary.QualCodeColumn;
                workspace.ApprovalStatusColumn = deserializedSummary.ApprovalStatusColumn;
                workspace.ExcludedApprovalStatusValue = deserializedSummary.ExcludedApprovalStatusValue;
                workspace.Control1OnlyMode = deserializedSummary.Control1OnlyMode;
                workspace.H16QualCodeColumn = deserializedSummary.H16QualCodeColumn;
                workspace.AuditQualCodeColumn = string.IsNullOrWhiteSpace(workspace.AuditQualCodeColumn)
                    ? deserializedSummary.AuditQualCodeColumn
                    : workspace.AuditQualCodeColumn;
            }

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

            return workspace;
        }

        public async Task<Rule24RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 24;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            var viewModel = new Rule24RunReviewViewModel
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

            viewModel.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, viewModel.ClientId, currentUserId.Value) ?? ""
                : "";
            viewModel.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            viewModel.HasDataAnalystSignoff = viewModel.Signoffs.Any(s =>
                string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            return viewModel;
        }

        public async Task<Rule24WorkspaceSaveResult> SaveWorkspaceAsync(Rule24ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                {
                    return new Rule24WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run the validation first so the workspace can be saved."
                    };
                }

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                {
                    return new Rule24WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "The saved workspace could not be found for this engagement."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, request.ClientId);
                await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 24);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                var previousHash = await GetValidationRecordHashAsync(connection, request.RunId.Value);
                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer = @HemisServer,
    AuditDatabase = @AuditDatabase,
    StudTable = @QualTable,
    DeceasedTable = @AuditTable,
    StudColumn = @H16Table,
    DeceasedColumn = @AuditQualCodeColumn,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review',
    IsCurrent = 1
WHERE RunID = @RunID
  AND ClientID = @ClientID;";
                command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                command.Parameters.AddWithValue("@ClientID", request.ClientId);
                command.Parameters.AddWithValue("@HemisServer", request.Server);
                command.Parameters.AddWithValue("@AuditDatabase", request.Database);
                command.Parameters.AddWithValue("@QualTable", request.QualTable);
                command.Parameters.AddWithValue("@AuditTable", request.AuditTable);
                command.Parameters.AddWithValue("@H16Table", request.H16Table);
                command.Parameters.AddWithValue("@AuditQualCodeColumn", request.AuditQualCodeColumn);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule24|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|{request.QualTable}|{request.AuditTable}|{request.H16Table}|{request.QualCodeColumn}|{request.ApprovalStatusColumn}|{request.ExcludedApprovalStatusValue}|{request.Control1OnlyMode}|{request.AuditQualCodeColumn}|{request.H16QualCodeColumn}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule24WorkspaceSaveResult
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
                return new Rule24WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule24WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule24WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Saved workspace was not found."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                await MarkPreviousRunsHistoricalAsync(connection, clientId.Value, 24);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash = await GetValidationRecordHashAsync(connection, runId);

                await using var markEdit = connection.CreateConfiguredCommand();
                markEdit.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review',
    IsCurrent = 1
WHERE RunID = @RunID;";
                markEdit.Parameters.AddWithValue("@RunID", runId);
                markEdit.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule24|{runId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule24WorkspaceSaveResult
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
                return new Rule24WorkspaceSaveResult
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
                throw new InvalidOperationException("Reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("Validation run was not found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

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

            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("Validation run was not found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var removal = await ReviewSignoffSqlHelper.RemoveReviewerSignoffAsync(connection, runId, reviewerId.Value);
            if (removal.RemovedCount <= 0)
                return;

            await UpdateRunStatusFromSignoffsAsync(connection, runId);

            if (string.Equals(removal.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                await SetRunCurrentStateAsync(connection, runId, false);
            }
        }

        public Task<string> GenerateSqlAsync(Rule24ValidationRequest request)
        {
            ValidateSqlRequest(request);

            var qualTable = Sanitise(request.QualTable);
            var auditTable = Sanitise(request.AuditTable);
            var h16Table = Sanitise(request.H16Table);
            var qualColumn = Sanitise(request.QualCodeColumn);
            var approvalStatusColumn = Sanitise(request.ApprovalStatusColumn);
            var auditColumn = Sanitise(request.AuditQualCodeColumn);
            var h16Column = Sanitise(request.H16QualCodeColumn);
            EnsureAuditTableLooksCorrect(auditTable);
            var excludedApprovalStatusValue = EscapeSqlLiteral(request.ExcludedApprovalStatusValue);
            var validationMode = request.Control1OnlyMode ? "CONTROL_1_ONLY" : "STANDARD";
            var approvalFilterLabel = request.Control1OnlyMode
                ? "Not applied (Control 1 only mode)"
                : $"QUAL.[{approvalStatusColumn}] <> '{excludedApprovalStatusValue}'";
            var whereClauseSql = request.Control1OnlyMode
                ? string.Empty
                : $"\nWHERE QUAL.[{approvalStatusColumn}] <> '{excludedApprovalStatusValue}'";
            var reconciliationStatusSql = request.Control1OnlyMode
                ? $@"CASE
        WHEN H16.[{h16Column}] IS NULL THEN 'MISSING_IN_H16'
        WHEN AUDIT.[{auditColumn}] IS NULL THEN 'MISSING_IN_AUDIT'
        ELSE 'NO_BLANKS'
    END"
                : $@"CASE
        WHEN H16.[{h16Column}] IS NULL THEN 'MISSING_IN_H16'
        WHEN AUDIT.[{auditColumn}] IS NULL THEN 'MISSING_IN_AUDIT'
        WHEN LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(STUD.[_001] AS nvarchar(255))))
             AND QUAL.[{approvalStatusColumn}] <> '{excludedApprovalStatusValue}'
             AND H16.[{h16Column}] IS NOT NULL
             AND AUDIT.[{auditColumn}] IS NOT NULL THEN 'MATCH'
        ELSE 'MISMATCH'
    END";
            var issueDescriptionSql = request.Control1OnlyMode
                ? $@"CASE
        WHEN H16.[{h16Column}] IS NULL THEN 'H16 qualification record missing'
        WHEN AUDIT.[{auditColumn}] IS NULL THEN 'Audit qualification record missing'
        ELSE 'No blanks found in H16 or Audit'
    END"
                : $@"CASE
        WHEN H16.[{h16Column}] IS NULL THEN 'H16 qualification record missing'
        WHEN AUDIT.[{auditColumn}] IS NULL THEN 'Audit qualification record missing'
        WHEN LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(STUD.[_001] AS nvarchar(255))))
             AND QUAL.[{approvalStatusColumn}] <> '{excludedApprovalStatusValue}'
             AND H16.[{h16Column}] IS NOT NULL
             AND AUDIT.[{auditColumn}] IS NOT NULL THEN 'All qualifications match'
        ELSE 'Qualification code mismatch'
    END";
            var controlTypeSql = request.Control1OnlyMode
                ? "'CONTROL_1'"
                : $@"CASE
        WHEN H16.[{h16Column}] IS NULL OR AUDIT.[{auditColumn}] IS NULL THEN 'CONTROL_1'
        WHEN LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(STUD.[_001] AS nvarchar(255))))
             AND QUAL.[{approvalStatusColumn}] <> '{excludedApprovalStatusValue}' THEN 'CONTROL_2'
        ELSE 'CONTROL_1'
    END";

            var sql = $@"-- ================================================================
-- HEMIS RULE 24: RECONCILE QUALIFICATION DATASETS
-- ================================================================
-- Database: {request.Database}
-- QUAL Table: [{qualTable}]
-- Audit Table: [{auditTable}]
-- H16 Table: [{h16Table}]
-- Qualification Code Mapping: QUAL.[{qualColumn}] vs AUDIT.[{auditColumn}] vs H16.[{h16Column}]
-- Filter: {approvalFilterLabel}
-- Validation Mode: {validationMode}
-- ================================================================

-- CONTROL 1:
-- ISBLANK(H16QUAL.[{h16Column}]) OR ISBLANK(Prod_Qual.[{auditColumn}])
-- Step: Check if qualification exists in H16 or Audit tables

-- CONTROL 2:
-- dbo_QUAL.[{qualColumn}] = dbo_STUD.[_001] AND dbo_QUAL.[{approvalStatusColumn}] <> '{excludedApprovalStatusValue}'
-- Step: Verify qualification code matches student qualification
-- Note: Control 2 is {(request.Control1OnlyMode ? "SKIPPED because Control 1 only mode is enabled" : "ACTIVE")}

SELECT
    CAST(QUAL.[{qualColumn}] AS nvarchar(255)) AS QUAL_QualCode,
    CAST(QUAL.[{approvalStatusColumn}] AS nvarchar(255)) AS QUAL_ApprovalStatus,
    CAST(STUD.[_001] AS nvarchar(255)) AS STUD_QualCode,
    CAST(AUDIT.[{auditColumn}] AS nvarchar(255)) AS AUDIT_QualCode,
    CAST(H16.[{h16Column}] AS nvarchar(255)) AS H16_QualCode,
    {reconciliationStatusSql} AS Reconciliation_Status,
    {issueDescriptionSql} AS Issue_Description,
    {controlTypeSql} AS Control_Type
FROM [dbo_STUD] STUD
LEFT JOIN [{qualTable}] QUAL
    ON LTRIM(RTRIM(CAST(STUD.[_001] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255))))
LEFT JOIN [{auditTable}] AUDIT
    ON LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(AUDIT.[{auditColumn}] AS nvarchar(255))))
LEFT JOIN [{h16Table}] H16
    ON LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(H16.[{h16Column}] AS nvarchar(255))))
{whereClauseSql}
ORDER BY QUAL_QualCode, AUDIT_QualCode, H16_QualCode;

SELECT
    Reconciliation_Status,
    COUNT(*) AS Issue_Count
FROM (
    SELECT
        {reconciliationStatusSql} AS Reconciliation_Status
    FROM [dbo_STUD] STUD
    LEFT JOIN [{qualTable}] QUAL
        ON LTRIM(RTRIM(CAST(STUD.[_001] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255))))
    LEFT JOIN [{auditTable}] AUDIT
        ON LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(AUDIT.[{auditColumn}] AS nvarchar(255))))
    LEFT JOIN [{h16Table}] H16
        ON LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(H16.[{h16Column}] AS nvarchar(255))))
    {whereClauseSql}
) Results
GROUP BY Reconciliation_Status
ORDER BY Issue_Count DESC;";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule24ValidationSummary> AnalyseAsync(Rule24ValidationRequest request)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var qualTable = Sanitise(request.QualTable);
            var auditTable = Sanitise(request.AuditTable);
            var h16Table = Sanitise(request.H16Table);
            var qualColumn = Sanitise(request.QualCodeColumn);
            var approvalStatusColumn = Sanitise(request.ApprovalStatusColumn);
            var auditColumn = Sanitise(request.AuditQualCodeColumn);
            var h16Column = Sanitise(request.H16QualCodeColumn);
            EnsureAuditTableLooksCorrect(auditTable);
            var passStatus = request.Control1OnlyMode ? "NO_BLANKS" : "MATCH";
            var whereClauseSql = request.Control1OnlyMode
                ? string.Empty
                : $"\nWHERE CAST(QUAL.[{approvalStatusColumn}] AS nvarchar(255)) <> @ExcludedApprovalStatusValue";
            var reconciliationStatusSql = request.Control1OnlyMode
                ? $@"CASE
        WHEN H16.[{h16Column}] IS NULL THEN 'MISSING_IN_H16'
        WHEN AUDIT.[{auditColumn}] IS NULL THEN 'MISSING_IN_AUDIT'
        ELSE 'NO_BLANKS'
    END"
                : $@"CASE
        WHEN H16.[{h16Column}] IS NULL THEN 'MISSING_IN_H16'
        WHEN AUDIT.[{auditColumn}] IS NULL THEN 'MISSING_IN_AUDIT'
        WHEN LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(STUD.[_001] AS nvarchar(255))))
         AND CAST(QUAL.[{approvalStatusColumn}] AS nvarchar(255)) <> @ExcludedApprovalStatusValue
         AND H16.[{h16Column}] IS NOT NULL
         AND AUDIT.[{auditColumn}] IS NOT NULL THEN 'MATCH'
        ELSE 'MISMATCH'
    END";
            var issueDescriptionSql = request.Control1OnlyMode
                ? $@"CASE
        WHEN H16.[{h16Column}] IS NULL THEN 'H16 qualification record missing'
        WHEN AUDIT.[{auditColumn}] IS NULL THEN 'Audit qualification record missing'
        ELSE 'No blanks found in H16 or Audit'
    END"
                : $@"CASE
        WHEN H16.[{h16Column}] IS NULL THEN 'H16 qualification record missing'
        WHEN AUDIT.[{auditColumn}] IS NULL THEN 'Audit qualification record missing'
        WHEN LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(STUD.[_001] AS nvarchar(255))))
         AND CAST(QUAL.[{approvalStatusColumn}] AS nvarchar(255)) <> @ExcludedApprovalStatusValue
         AND H16.[{h16Column}] IS NOT NULL
         AND AUDIT.[{auditColumn}] IS NOT NULL THEN 'All qualifications match'
        ELSE 'Qualification code mismatch'
    END";
            var controlTypeSql = request.Control1OnlyMode
                ? "'CONTROL_1'"
                : $@"CASE
        WHEN H16.[{h16Column}] IS NULL OR AUDIT.[{auditColumn}] IS NULL THEN 'CONTROL_1'
        WHEN LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(STUD.[_001] AS nvarchar(255))))
         AND CAST(QUAL.[{approvalStatusColumn}] AS nvarchar(255)) <> @ExcludedApprovalStatusValue THEN 'CONTROL_2'
        ELSE 'CONTROL_1'
    END";

            var qualCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{qualTable}];");
            var auditCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{auditTable}];");
            var h16Count = await CountAsync(conn, $"SELECT COUNT(*) FROM [{h16Table}];");

            await using var command = conn.CreateConfiguredCommand();
            command.CommandText = $@"
SELECT
    CAST(QUAL.[{qualColumn}] AS nvarchar(255)) AS QUAL_QualCode,
    CAST(QUAL.[{approvalStatusColumn}] AS nvarchar(255)) AS QUAL_ApprovalStatus,
    CAST(STUD.[_001] AS nvarchar(255)) AS STUD_QualCode,
    CAST(AUDIT.[{auditColumn}] AS nvarchar(255)) AS AUDIT_QualCode,
    CAST(H16.[{h16Column}] AS nvarchar(255)) AS H16_QualCode,
    {reconciliationStatusSql} AS Reconciliation_Status,
    {issueDescriptionSql} AS Issue_Description,
    {controlTypeSql} AS Control_Type
FROM [dbo_STUD] STUD
LEFT JOIN [{qualTable}] QUAL
    ON LTRIM(RTRIM(CAST(STUD.[_001] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255))))
LEFT JOIN [{auditTable}] AUDIT
    ON LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(AUDIT.[{auditColumn}] AS nvarchar(255))))
LEFT JOIN [{h16Table}] H16
    ON LTRIM(RTRIM(CAST(QUAL.[{qualColumn}] AS nvarchar(255)))) = LTRIM(RTRIM(CAST(H16.[{h16Column}] AS nvarchar(255))))
{whereClauseSql}
ORDER BY QUAL_QualCode, AUDIT_QualCode, H16_QualCode;";
            if (!request.Control1OnlyMode)
            {
                command.Parameters.AddWithValue("@ExcludedApprovalStatusValue", request.ExcludedApprovalStatusValue);
            }

            await using var reader = await command.ExecuteReaderAsync();
            var passSampleRows = new List<Rule24ReconciliationRowViewModel>();
            var failRows = new List<Rule24ReconciliationRowViewModel>();
            var issueCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var totalValidated = 0;
            var matches = 0;
            var mismatches = 0;

            while (await reader.ReadAsync())
            {
                totalValidated++;
                var row = new Rule24ReconciliationRowViewModel
                {
                    ValidationNumber = totalValidated,
                    QualQualCode = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    QualApprovalStatus = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    StudQualCode = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    AuditQualCode = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    H16QualCode = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    ReconciliationStatus = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    IssueDescription = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    ControlType = reader.IsDBNull(7) ? "" : reader.GetString(7)
                };

                if (string.Equals(row.ReconciliationStatus, passStatus, StringComparison.OrdinalIgnoreCase))
                {
                    matches++;
                    if (passSampleRows.Count < PassSampleLimit)
                        passSampleRows.Add(row);
                }
                else
                {
                    mismatches++;
                    failRows.Add(row);
                    issueCounts[row.ReconciliationStatus] = issueCounts.TryGetValue(row.ReconciliationStatus, out var current)
                        ? current + 1
                        : 1;
                }
            }

            var exceptionRate = totalValidated == 0
                ? 0m
                : Math.Round((decimal)mismatches / totalValidated * 100m, 2);
            var matchRate = totalValidated == 0
                ? 0m
                : Math.Round((decimal)matches / totalValidated * 100m, 2);

            return new Rule24ValidationSummary
            {
                Success = true,
                TotalValidated = totalValidated,
                Matches = matches,
                Mismatches = mismatches,
                PassCount = matches,
                FailCount = mismatches,
                ExceptionRate = exceptionRate,
                MatchRate = matchRate,
                Status = mismatches == 0 ? "PASS" : "FAIL",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                QualTable = request.QualTable,
                AuditTable = request.AuditTable,
                H16Table = request.H16Table,
                QualCodeColumn = request.QualCodeColumn,
                ApprovalStatusColumn = request.ApprovalStatusColumn,
                ExcludedApprovalStatusValue = request.ExcludedApprovalStatusValue,
                Control1OnlyMode = request.Control1OnlyMode,
                AuditQualCodeColumn = request.AuditQualCodeColumn,
                H16QualCodeColumn = request.H16QualCodeColumn,
                QualCount = qualCount,
                AuditCount = auditCount,
                H16Count = h16Count,
                ClientId = request.ClientId,
                PassSampleCount = passSampleRows.Count,
                PassSampleTruncated = matches > passSampleRows.Count,
                IssueCounts = issueCounts
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new Rule24IssueBreakdownItemViewModel
                    {
                        Status = x.Key,
                        Count = x.Value
                    })
                    .ToList(),
                PassSampleRows = passSampleRows,
                FailRows = failRows
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule24ValidationRequest request, Rule24ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 24);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 24);

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
    @ClientID, @UserID, 24, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @QualTable, @AuditTable, @H16Table, @AuditQualCodeColumn,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "Reconcile Qualification Datasets");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@QualTable", request.QualTable);
            command.Parameters.AddWithValue("@AuditTable", request.AuditTable);
            command.Parameters.AddWithValue("@H16Table", request.H16Table);
            command.Parameters.AddWithValue("@AuditQualCodeColumn", request.AuditQualCodeColumn);
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary.FailRows)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
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
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule24|{runId}|{request.ClientId}|{systemUserId.Value}|{request.QualTable}|{request.QualCodeColumn}|{request.ApprovalStatusColumn}|{request.ExcludedApprovalStatusValue}|{request.Control1OnlyMode}|{request.AuditTable}|{request.AuditQualCodeColumn}|{request.H16Table}|{request.H16QualCodeColumn}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
        }

        private async Task EnsureColumnsExistAsync(string server, string database, string driver, Rule24VerifyRequest request)
        {
            var qualColumns = await GetTableColumnsAsync(server, database, driver, request.QualTable);
            var auditColumns = await GetTableColumnsAsync(server, database, driver, request.AuditTable);
            var h16Columns = await GetTableColumnsAsync(server, database, driver, request.H16Table);
            var studColumns = await GetTableColumnsAsync(server, database, driver, "dbo_STUD");

            if (!qualColumns.Any(c => c.Equals(request.QualCodeColumn, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Column '{request.QualCodeColumn}' was not found on table '{request.QualTable}'.");
            if (!qualColumns.Any(c => c.Equals("_004", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Column '_004' was not found on table '{request.QualTable}'.");
            if (!auditColumns.Any(c => c.Equals(request.AuditQualCodeColumn, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Column '{request.AuditQualCodeColumn}' was not found on table '{request.AuditTable}'.");
            if (!h16Columns.Any(c => c.Equals(request.H16QualCodeColumn, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Column '{request.H16QualCodeColumn}' was not found on table '{request.H16Table}'.");
            if (!studColumns.Any(c => c.Equals("_001", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Column '_001' was not found on table 'dbo_STUD'.");
        }

        private Task EnsureColumnsExistAsync(string server, string database, string driver, Rule24ValidationRequest request) =>
            EnsureColumnsExistAsync(server, database, driver, new Rule24VerifyRequest
            {
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                QualTable = request.QualTable,
                AuditTable = request.AuditTable,
                H16Table = request.H16Table,
                QualCodeColumn = request.QualCodeColumn,
                AuditQualCodeColumn = request.AuditQualCodeColumn,
                H16QualCodeColumn = request.H16QualCodeColumn
            });

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

        private static void ValidateRequest(Rule24ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.QualTable))
                throw new InvalidOperationException("QUAL table is required.");
            if (string.IsNullOrWhiteSpace(request.AuditTable))
                throw new InvalidOperationException("Audit table is required.");
            if (string.IsNullOrWhiteSpace(request.H16Table))
                throw new InvalidOperationException("H16 table is required.");
            if (string.IsNullOrWhiteSpace(request.QualCodeColumn))
                throw new InvalidOperationException("QUAL qualification code column is required.");
            if (string.IsNullOrWhiteSpace(request.ApprovalStatusColumn))
                throw new InvalidOperationException("Approval status column is required.");
            if (string.IsNullOrWhiteSpace(request.ExcludedApprovalStatusValue))
                throw new InvalidOperationException("Excluded approval status value is required.");
            if (string.IsNullOrWhiteSpace(request.AuditQualCodeColumn))
                throw new InvalidOperationException("Audit qualification code column is required.");
            if (string.IsNullOrWhiteSpace(request.H16QualCodeColumn))
                throw new InvalidOperationException("H16 qualification code column is required.");

            ValidateObjectName(request.QualTable);
            ValidateObjectName(request.AuditTable);
            ValidateObjectName(request.H16Table);
            ValidateObjectName(request.QualCodeColumn);
            ValidateObjectName(request.ApprovalStatusColumn);
            ValidateObjectName(request.AuditQualCodeColumn);
            ValidateObjectName(request.H16QualCodeColumn);
        }

        private static void ValidateRequest(Rule24VerifyRequest request)
        {
            ValidateRequest(new Rule24ValidationRequest
            {
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                QualTable = request.QualTable,
                AuditTable = request.AuditTable,
                H16Table = request.H16Table,
                QualCodeColumn = request.QualCodeColumn,
                ApprovalStatusColumn = request.ApprovalStatusColumn,
                ExcludedApprovalStatusValue = request.ExcludedApprovalStatusValue,
                AuditQualCodeColumn = request.AuditQualCodeColumn,
                H16QualCodeColumn = request.H16QualCodeColumn
            });
        }

        private static void ValidateSqlRequest(Rule24ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");

            ValidateRequest(new Rule24ValidationRequest
            {
                Server = "sql-preview",
                Database = request.Database,
                Driver = request.Driver,
                QualTable = request.QualTable,
                AuditTable = request.AuditTable,
                H16Table = request.H16Table,
                QualCodeColumn = request.QualCodeColumn,
                ApprovalStatusColumn = request.ApprovalStatusColumn,
                ExcludedApprovalStatusValue = request.ExcludedApprovalStatusValue,
                AuditQualCodeColumn = request.AuditQualCodeColumn,
                H16QualCodeColumn = request.H16QualCodeColumn
            });
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

        private static void EnsureAuditTableLooksCorrect(string auditTable)
        {
            var value = auditTable ?? "";
            if (value.Contains("STUD", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("CRSE", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The selected audit table is not a qualification audit table. Use MT-audit-prod-QUAL.");
            }
        }

        private static string? FindFirst(IEnumerable<string> values, string[] exactMatches, string[] containsMatches)
        {
            foreach (var exact in exactMatches)
            {
                var match = values.FirstOrDefault(v => v.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            foreach (var fragment in containsMatches)
            {
                var match = values.FirstOrDefault(v => v.Contains(fragment, StringComparison.OrdinalIgnoreCase));
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
            var clearedSignoffs = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            await using var deleteCommand = connection.CreateConfiguredCommand();
            deleteCommand.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            deleteCommand.Parameters.AddWithValue("@RunID", runId);
            await deleteCommand.ExecuteNonQueryAsync();

            await SetRunStatusAsync(connection, runId, "Needs Review");
            return clearedSignoffs;
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

        private async Task SetRunCurrentStateAsync(SqlConnection connection, int runId, bool isCurrent)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET IsCurrent = @IsCurrent WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@IsCurrent", isCurrent);
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

        private async Task<Rule24ValidationSummary?> GetValidationSummaryAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ResultsJSON FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value
                ? null
                : DeserializeSummary(Convert.ToString(value));
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

        private static string EscapeSqlLiteral(string value) =>
            (value ?? "").Replace("'", "''");

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

        private static Rule24ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule24ValidationSummary>(decoded);
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyBrowserPreview(Rule24ValidationSummary summary)
        {
            summary.PassSampleRows = summary.PassSampleRows
                .Take(BrowserPreviewRowLimit)
                .ToList();
            summary.FailRows = summary.FailRows
                .Take(BrowserPreviewRowLimit)
                .ToList();
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
    }
}
