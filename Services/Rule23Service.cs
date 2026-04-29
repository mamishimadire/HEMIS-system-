using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Security.Cryptography;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule23Service : IRule23Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private const int PassSampleLimit = 100;
        private const int FailRowLimit = 5000;
        private const int SqlCommandTimeoutSeconds = SqlLargeDataExtensions.LargeDataCommandTimeoutSeconds;
        private readonly IConfiguration _configuration;

        public Rule23Service(IConfiguration configuration)
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

        public async Task<Rule23TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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

                return new Rule23TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = FindFirst(tables,
                        ["dbo_STUD", "STUD", "dbo_STUD_VALIDATION_DETAIL"],
                        ["stud"]),
                    AutoAuditTable = FindFirst(tables,
                        ["MT-audit-prod-std", "MT_AUDIT_PROD_STD"],
                        ["audit", "std"]),
                    AutoH16Table = FindFirst(tables,
                        ["H16STUD", "H16STD"],
                        ["h16", "stud"])
                };
            }
            catch (Exception ex)
            {
                return new Rule23TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule23AuditColumnResult> GetAuditColumnsAsync(string server, string database, string driver, string auditTable)
        {
            try
            {
                var columns = await GetTableColumnsAsync(server, database, driver, auditTable);
                return new Rule23AuditColumnResult
                {
                    Success = true,
                    Columns = columns,
                    AutoStudentNumberColumn = FindFirst(columns,
                        ["IAGSTNO", "STUDNUM", "STUDENTNUMBER"],
                        ["iagstno", "studnum", "student"]),
                    AutoQualificationColumn = FindFirst(columns,
                        ["IAGQUAL", "QUALCODE", "QUAL"],
                        ["iagqual", "qualcode", "qual"]),
                    AutoIdNumberColumn = FindFirst(columns,
                        ["IADIDNO", "SAIDNUM", "IDNUMBER"],
                        ["iadidno", "saidnum", "id"])
                };
            }
            catch (Exception ex)
            {
                return new Rule23AuditColumnResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule23VerifyResult> VerifyTablesAsync(Rule23VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request);

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var studTable = Sanitise(request.StudTable);
                var auditTable = Sanitise(request.AuditTable);
                var h16Table = Sanitise(request.H16Table);

                return new Rule23VerifyResult
                {
                    Success = true,
                    StudCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{studTable}];"),
                    AuditCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{auditTable}];"),
                    H16Count = await CountAsync(conn, $"SELECT COUNT(*) FROM [{h16Table}];")
                };
            }
            catch (Exception ex)
            {
                return new Rule23VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule23ValidationSummary> RunValidationAsync(Rule23ValidationRequest request, string? userEmail = null, string? userName = null)
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
                return new Rule23ValidationSummary
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

        public async Task<Rule23WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
    ISNULL(vr.DeceasedTable, '') AS AuditTable,
    ISNULL(vr.StudColumn, '') AS H16Table,
    ISNULL(vr.DeceasedColumn, '') AS AuditStudentNumberColumn,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 23
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

            var workspace = new Rule23WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                StudTable = reader.IsDBNull(4) ? "" : reader.GetString(4),
                AuditTable = reader.IsDBNull(5) ? "" : reader.GetString(5),
                H16Table = reader.IsDBNull(6) ? "" : reader.GetString(6),
                AuditStudentNumberColumn = reader.IsDBNull(7) ? "IAGSTNO" : reader.GetString(7),
                CurrentStatus = reader.IsDBNull(8) ? "" : reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9) ? null : reader.GetString(9),
                LastEditedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Summary = summary
            };

            if (deserializedSummary != null)
            {
                workspace.StudStudentNumberColumn = deserializedSummary.StudStudentNumberColumn;
                workspace.StudQualificationColumn = deserializedSummary.StudQualificationColumn;
                workspace.StudIdNumberColumn = deserializedSummary.StudIdNumberColumn;
                workspace.AuditStudentNumberColumn = string.IsNullOrWhiteSpace(workspace.AuditStudentNumberColumn)
                    ? deserializedSummary.AuditStudentNumberColumn
                    : workspace.AuditStudentNumberColumn;
                workspace.AuditQualificationColumn = deserializedSummary.AuditQualificationColumn;
                workspace.AuditIdNumberColumn = deserializedSummary.AuditIdNumberColumn;
                workspace.H16StudentNumberColumn = deserializedSummary.H16StudentNumberColumn;
                workspace.H16QualificationColumn = deserializedSummary.H16QualificationColumn;
                workspace.H16IdNumberColumn = deserializedSummary.H16IdNumberColumn;
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

        public async Task<Rule23RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 23;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            var viewModel = new Rule23RunReviewViewModel
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

        public async Task<Rule23WorkspaceSaveResult> SaveWorkspaceAsync(Rule23ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                {
                    return new Rule23WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run the validation first so the workspace can be saved."
                    };
                }

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                {
                    return new Rule23WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "The saved workspace could not be found for this engagement."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, request.ClientId);
                await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 23);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                var previousHash = await GetValidationRecordHashAsync(connection, request.RunId.Value);
                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer = @HemisServer,
    AuditDatabase = @AuditDatabase,
    StudTable = @StudTable,
    DeceasedTable = @AuditTable,
    StudColumn = @H16Table,
    DeceasedColumn = @AuditStudentNumberColumn,
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
                command.Parameters.AddWithValue("@StudTable", request.StudTable);
                command.Parameters.AddWithValue("@AuditTable", request.AuditTable);
                command.Parameters.AddWithValue("@H16Table", request.H16Table);
                command.Parameters.AddWithValue("@AuditStudentNumberColumn", request.AuditStudentNumberColumn);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule23|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|{request.StudTable}|{request.AuditTable}|{request.H16Table}|{request.StudStudentNumberColumn}|{request.StudQualificationColumn}|{request.StudIdNumberColumn}|{request.AuditStudentNumberColumn}|{request.AuditQualificationColumn}|{request.AuditIdNumberColumn}|{request.H16StudentNumberColumn}|{request.H16QualificationColumn}|{request.H16IdNumberColumn}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule23WorkspaceSaveResult
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
                return new Rule23WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule23WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule23WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Saved workspace was not found."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                await MarkPreviousRunsHistoricalAsync(connection, clientId.Value, 23);

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
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule23|{runId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule23WorkspaceSaveResult
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
                return new Rule23WorkspaceSaveResult
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

        public Task<string> GenerateSqlAsync(Rule23ValidationRequest request)
        {
            ValidateSqlRequest(request);

            var studTable = Sanitise(request.StudTable);
            var auditTable = Sanitise(request.AuditTable);
            var h16Table = Sanitise(request.H16Table);
            var studStudentColumn = Sanitise(request.StudStudentNumberColumn);
            var studQualColumn = Sanitise(request.StudQualificationColumn);
            var studIdColumn = Sanitise(request.StudIdNumberColumn);
            var auditStudentColumn = Sanitise(request.AuditStudentNumberColumn);
            var auditQualColumn = Sanitise(request.AuditQualificationColumn);
            var auditIdColumn = Sanitise(request.AuditIdNumberColumn);
            var h16StudentColumn = Sanitise(request.H16StudentNumberColumn);
            var h16QualColumn = Sanitise(request.H16QualificationColumn);
            var h16IdColumn = Sanitise(request.H16IdNumberColumn);

            var sql = $@"-- ================================================================
-- HEMIS RULE 23: RECONCILE DATASETS
-- ================================================================
-- Database: {request.Database}
-- STUD Table: [{studTable}]
-- Audit Table: [{auditTable}]
-- H16 Table: [{h16Table}]
-- STUD Columns: Student# [{studStudentColumn}], Qual [{studQualColumn}], ID# [{studIdColumn}]
-- Audit Columns: Student# [{auditStudentColumn}], Qual [{auditQualColumn}], ID# [{auditIdColumn}]
-- H16 Columns: Student# [{h16StudentColumn}], Qual [{h16QualColumn}], ID# [{h16IdColumn}]
-- ================================================================

SELECT
    CAST(STUD.[{studStudentColumn}] AS nvarchar(255)) AS STUD_StudentNum,
    CAST(STUD.[{studQualColumn}] AS nvarchar(255)) AS STUD_QualCode,
    CAST(STUD.[{studIdColumn}] AS nvarchar(255)) AS STUD_IDNum,
    CAST(AUDIT.[{auditStudentColumn}] AS nvarchar(255)) AS AUDIT_StudentNum,
    CAST(AUDIT.[{auditQualColumn}] AS nvarchar(255)) AS AUDIT_QualCode,
    CAST(AUDIT.[{auditIdColumn}] AS nvarchar(255)) AS AUDIT_IDNum,
    CAST(H16.[{h16StudentColumn}] AS nvarchar(255)) AS H16_StudentNum,
    CAST(H16.[{h16QualColumn}] AS nvarchar(255)) AS H16_QualCode,
    CAST(H16.[{h16IdColumn}] AS nvarchar(255)) AS H16_IDNum,
    CASE 
        WHEN H16.[{h16StudentColumn}] IS NULL THEN 'MISSING_IN_H16'
        WHEN STUD.[{studIdColumn}] <> H16.[{h16IdColumn}] THEN 'ID_MISMATCH_H16'
        WHEN STUD.[{studQualColumn}] <> H16.[{h16QualColumn}] THEN 'QUAL_MISMATCH_H16'
        WHEN AUDIT.[{auditStudentColumn}] IS NULL THEN 'MISSING_IN_AUDIT'
        WHEN STUD.[{studIdColumn}] <> AUDIT.[{auditIdColumn}] THEN 'ID_MISMATCH_AUDIT'
        WHEN STUD.[{studQualColumn}] <> AUDIT.[{auditQualColumn}] THEN 'QUAL_MISMATCH_AUDIT'
        ELSE 'MATCH'
    END AS Reconciliation_Status,
    CASE
        WHEN H16.[{h16StudentColumn}] IS NULL THEN 'H16 record missing'
        WHEN STUD.[{studIdColumn}] <> H16.[{h16IdColumn}] THEN 'ID mismatch with H16'
        WHEN STUD.[{studQualColumn}] <> H16.[{h16QualColumn}] THEN 'Qualification mismatch with H16'
        WHEN AUDIT.[{auditStudentColumn}] IS NULL THEN 'Audit record missing'
        WHEN STUD.[{studIdColumn}] <> AUDIT.[{auditIdColumn}] THEN 'ID mismatch with Audit'
        WHEN STUD.[{studQualColumn}] <> AUDIT.[{auditQualColumn}] THEN 'Qualification mismatch with Audit'
        ELSE 'All records match'
    END AS Issue_Description
FROM [{studTable}] STUD
LEFT JOIN [{auditTable}] AUDIT
    ON STUD.[{studStudentColumn}] = AUDIT.[{auditStudentColumn}]
LEFT JOIN [{h16Table}] H16
    ON STUD.[{studStudentColumn}] = H16.[{h16StudentColumn}]
WHERE
    H16.[{h16StudentColumn}] IS NULL
    OR STUD.[{studIdColumn}] <> H16.[{h16IdColumn}]
    OR STUD.[{studQualColumn}] <> H16.[{h16QualColumn}]
    OR AUDIT.[{auditStudentColumn}] IS NULL
    OR STUD.[{studIdColumn}] <> AUDIT.[{auditIdColumn}]
    OR STUD.[{studQualColumn}] <> AUDIT.[{auditQualColumn}];

SELECT
    Reconciliation_Status,
    COUNT(*) AS Issue_Count
FROM (
    SELECT
        CASE
            WHEN H16.[{h16StudentColumn}] IS NULL THEN 'MISSING_IN_H16'
            WHEN STUD.[{studIdColumn}] <> H16.[{h16IdColumn}] THEN 'ID_MISMATCH_H16'
            WHEN STUD.[{studQualColumn}] <> H16.[{h16QualColumn}] THEN 'QUAL_MISMATCH_H16'
            WHEN AUDIT.[{auditStudentColumn}] IS NULL THEN 'MISSING_IN_AUDIT'
            WHEN STUD.[{studIdColumn}] <> AUDIT.[{auditIdColumn}] THEN 'ID_MISMATCH_AUDIT'
            WHEN STUD.[{studQualColumn}] <> AUDIT.[{auditQualColumn}] THEN 'QUAL_MISMATCH_AUDIT'
            ELSE 'MATCH'
        END AS Reconciliation_Status
    FROM [{studTable}] STUD
    LEFT JOIN [{auditTable}] AUDIT
        ON STUD.[{studStudentColumn}] = AUDIT.[{auditStudentColumn}]
    LEFT JOIN [{h16Table}] H16
        ON STUD.[{studStudentColumn}] = H16.[{h16StudentColumn}]
    WHERE
        H16.[{h16StudentColumn}] IS NULL
        OR STUD.[{studIdColumn}] <> H16.[{h16IdColumn}]
        OR STUD.[{studQualColumn}] <> H16.[{h16QualColumn}]
        OR AUDIT.[{auditStudentColumn}] IS NULL
        OR STUD.[{studIdColumn}] <> AUDIT.[{auditIdColumn}]
        OR STUD.[{studQualColumn}] <> AUDIT.[{auditQualColumn}]
) Results
GROUP BY Reconciliation_Status
ORDER BY Issue_Count DESC;";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule23ValidationSummary> AnalyseAsync(Rule23ValidationRequest request)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var studTable = Sanitise(request.StudTable);
            var auditTable = Sanitise(request.AuditTable);
            var h16Table = Sanitise(request.H16Table);
            var studStudentColumn = Sanitise(request.StudStudentNumberColumn);
            var studQualColumn = Sanitise(request.StudQualificationColumn);
            var studIdColumn = Sanitise(request.StudIdNumberColumn);
            var auditStudentColumn = Sanitise(request.AuditStudentNumberColumn);
            var auditQualColumn = Sanitise(request.AuditQualificationColumn);
            var auditIdColumn = Sanitise(request.AuditIdNumberColumn);
            var h16StudentColumn = Sanitise(request.H16StudentNumberColumn);
            var h16QualColumn = Sanitise(request.H16QualificationColumn);
            var h16IdColumn = Sanitise(request.H16IdNumberColumn);

            var studCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{studTable}];");
            var auditCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{auditTable}];");
            var h16Count = await CountAsync(conn, $"SELECT COUNT(*) FROM [{h16Table}];");
            var reconciliationCte = BuildReconciliationCte(
                studTable,
                auditTable,
                h16Table,
                studStudentColumn,
                studQualColumn,
                studIdColumn,
                auditStudentColumn,
                auditQualColumn,
                auditIdColumn,
                h16StudentColumn,
                h16QualColumn,
                h16IdColumn);

            var issueCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var totalValidated = 0;
            var matches = 0;

            await using (var summaryCommand = conn.CreateConfiguredCommand())
            {
                summaryCommand.CommandTimeout = SqlCommandTimeoutSeconds;
                summaryCommand.CommandText = $@"
{reconciliationCte}
SELECT
    Reconciliation_Status,
    COUNT(*) AS Issue_Count
FROM Reconciliation
GROUP BY Reconciliation_Status;";

                await using var summaryReader = await summaryCommand.ExecuteReaderAsync();
                while (await summaryReader.ReadAsync())
                {
                    var status = summaryReader.IsDBNull(0) ? "" : summaryReader.GetString(0);
                    var count = summaryReader.IsDBNull(1) ? 0 : summaryReader.GetInt32(1);
                    totalValidated += count;

                    if (string.Equals(status, "MATCH", StringComparison.OrdinalIgnoreCase))
                    {
                        matches = count;
                    }
                    else if (!string.IsNullOrWhiteSpace(status))
                    {
                        issueCounts[status] = count;
                    }
                }
            }

            var mismatches = totalValidated - matches;
            var passSampleRows = await LoadReconciliationRowsAsync(
                conn,
                reconciliationCte,
                "WHERE Reconciliation_Status = 'MATCH'",
                PassSampleLimit);

            var failRows = await LoadReconciliationRowsAsync(
                conn,
                reconciliationCte,
                "WHERE Reconciliation_Status <> 'MATCH'",
                FailRowLimit);

            var exceptionRate = totalValidated == 0
                ? 0m
                : Math.Round((decimal)mismatches / totalValidated * 100m, 2);
            var matchRate = totalValidated == 0
                ? 0m
                : Math.Round((decimal)matches / totalValidated * 100m, 2);
            var failRowsTruncated = mismatches > failRows.Count;
            string? warning = null;

            if (failRowsTruncated)
            {
                warning = $"Only the first {failRows.Count:N0} mismatch rows were saved for browser review and export performance. Total mismatches found: {mismatches:N0}.";
            }

            return new Rule23ValidationSummary
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
                StudTable = request.StudTable,
                AuditTable = request.AuditTable,
                H16Table = request.H16Table,
                StudStudentNumberColumn = request.StudStudentNumberColumn,
                StudQualificationColumn = request.StudQualificationColumn,
                StudIdNumberColumn = request.StudIdNumberColumn,
                AuditStudentNumberColumn = request.AuditStudentNumberColumn,
                AuditQualificationColumn = request.AuditQualificationColumn,
                AuditIdNumberColumn = request.AuditIdNumberColumn,
                H16StudentNumberColumn = request.H16StudentNumberColumn,
                H16QualificationColumn = request.H16QualificationColumn,
                H16IdNumberColumn = request.H16IdNumberColumn,
                StudCount = studCount,
                AuditCount = auditCount,
                H16Count = h16Count,
                ClientId = request.ClientId,
                PassSampleCount = passSampleRows.Count,
                PassSampleTruncated = matches > passSampleRows.Count,
                SavedFailRowCount = failRows.Count,
                FailRowsTruncated = failRowsTruncated,
                IssueCounts = issueCounts
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new Rule23IssueBreakdownItemViewModel
                    {
                        Status = x.Key,
                        Count = x.Value
                    })
                    .ToList(),
                PassSampleRows = passSampleRows,
                FailRows = failRows,
                Warning = warning
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule23ValidationRequest request, Rule23ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 23);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 23);

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
    @ClientID, @UserID, 23, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @AuditTable, @H16Table, @AuditStudentNumberColumn,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "Reconcile Datasets");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.StudTable);
            command.Parameters.AddWithValue("@AuditTable", request.AuditTable);
            command.Parameters.AddWithValue("@H16Table", request.H16Table);
            command.Parameters.AddWithValue("@AuditStudentNumberColumn", request.AuditStudentNumberColumn);
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
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule23|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
        }

        private async Task EnsureColumnsExistAsync(string server, string database, string driver, Rule23VerifyRequest request)
        {
            var studColumns = await GetTableColumnsAsync(server, database, driver, request.StudTable);
            var auditColumns = await GetTableColumnsAsync(server, database, driver, request.AuditTable);
            var h16Columns = await GetTableColumnsAsync(server, database, driver, request.H16Table);

            foreach (var studColumn in new[] { request.StudStudentNumberColumn, request.StudQualificationColumn, request.StudIdNumberColumn })
            {
                if (!studColumns.Any(c => c.Equals(studColumn, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Column '{studColumn}' was not found on table '{request.StudTable}'.");
            }

            foreach (var auditColumn in new[] { request.AuditStudentNumberColumn, request.AuditQualificationColumn, request.AuditIdNumberColumn })
            {
                if (!auditColumns.Any(c => c.Equals(auditColumn, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Column '{auditColumn}' was not found on table '{request.AuditTable}'.");
            }

            foreach (var h16Column in new[] { request.H16StudentNumberColumn, request.H16QualificationColumn, request.H16IdNumberColumn })
            {
                if (!h16Columns.Any(c => c.Equals(h16Column, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Column '{h16Column}' was not found on table '{request.H16Table}'.");
            }
        }

        private Task EnsureColumnsExistAsync(string server, string database, string driver, Rule23ValidationRequest request) =>
            EnsureColumnsExistAsync(server, database, driver, new Rule23VerifyRequest
            {
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                StudTable = request.StudTable,
                AuditTable = request.AuditTable,
                H16Table = request.H16Table,
                StudStudentNumberColumn = request.StudStudentNumberColumn,
                StudQualificationColumn = request.StudQualificationColumn,
                StudIdNumberColumn = request.StudIdNumberColumn,
                AuditStudentNumberColumn = request.AuditStudentNumberColumn,
                AuditQualificationColumn = request.AuditQualificationColumn,
                AuditIdNumberColumn = request.AuditIdNumberColumn,
                H16StudentNumberColumn = request.H16StudentNumberColumn,
                H16QualificationColumn = request.H16QualificationColumn,
                H16IdNumberColumn = request.H16IdNumberColumn
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

        private static void ValidateRequest(Rule23ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.StudTable))
                throw new InvalidOperationException("STUD table is required.");
            if (string.IsNullOrWhiteSpace(request.AuditTable))
                throw new InvalidOperationException("Audit table is required.");
            if (string.IsNullOrWhiteSpace(request.H16Table))
                throw new InvalidOperationException("H16 table is required.");
            foreach (var column in new[]
            {
                request.StudStudentNumberColumn,
                request.StudQualificationColumn,
                request.StudIdNumberColumn,
                request.AuditStudentNumberColumn,
                request.AuditQualificationColumn,
                request.AuditIdNumberColumn,
                request.H16StudentNumberColumn,
                request.H16QualificationColumn,
                request.H16IdNumberColumn
            })
            {
                if (string.IsNullOrWhiteSpace(column))
                    throw new InvalidOperationException("All Rule 23 column mappings are required.");
            }

            ValidateObjectName(request.StudTable);
            ValidateObjectName(request.AuditTable);
            ValidateObjectName(request.H16Table);
            ValidateObjectName(request.StudStudentNumberColumn);
            ValidateObjectName(request.StudQualificationColumn);
            ValidateObjectName(request.StudIdNumberColumn);
            ValidateObjectName(request.AuditStudentNumberColumn);
            ValidateObjectName(request.AuditQualificationColumn);
            ValidateObjectName(request.AuditIdNumberColumn);
            ValidateObjectName(request.H16StudentNumberColumn);
            ValidateObjectName(request.H16QualificationColumn);
            ValidateObjectName(request.H16IdNumberColumn);
        }

        private static void ValidateRequest(Rule23VerifyRequest request)
        {
            ValidateRequest(new Rule23ValidationRequest
            {
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                StudTable = request.StudTable,
                AuditTable = request.AuditTable,
                H16Table = request.H16Table,
                StudStudentNumberColumn = request.StudStudentNumberColumn,
                StudQualificationColumn = request.StudQualificationColumn,
                StudIdNumberColumn = request.StudIdNumberColumn,
                AuditStudentNumberColumn = request.AuditStudentNumberColumn,
                AuditQualificationColumn = request.AuditQualificationColumn,
                AuditIdNumberColumn = request.AuditIdNumberColumn,
                H16StudentNumberColumn = request.H16StudentNumberColumn,
                H16QualificationColumn = request.H16QualificationColumn,
                H16IdNumberColumn = request.H16IdNumberColumn
            });
        }

        private static void ValidateSqlRequest(Rule23ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");

            ValidateRequest(new Rule23ValidationRequest
            {
                Server = "sql-preview",
                Database = request.Database,
                Driver = request.Driver,
                StudTable = request.StudTable,
                AuditTable = request.AuditTable,
                H16Table = request.H16Table,
                StudStudentNumberColumn = request.StudStudentNumberColumn,
                StudQualificationColumn = request.StudQualificationColumn,
                StudIdNumberColumn = request.StudIdNumberColumn,
                AuditStudentNumberColumn = request.AuditStudentNumberColumn,
                AuditQualificationColumn = request.AuditQualificationColumn,
                AuditIdNumberColumn = request.AuditIdNumberColumn,
                H16StudentNumberColumn = request.H16StudentNumberColumn,
                H16QualificationColumn = request.H16QualificationColumn,
                H16IdNumberColumn = request.H16IdNumberColumn
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

        private async Task<Rule23ValidationSummary?> GetValidationSummaryAsync(SqlConnection connection, int runId)
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

        private static async Task<int> CountAsync(SqlConnection connection, string sql)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private static string BuildReconciliationCte(
            string studTable,
            string auditTable,
            string h16Table,
            string studStudentColumn,
            string studQualColumn,
            string studIdColumn,
            string auditStudentColumn,
            string auditQualColumn,
            string auditIdColumn,
            string h16StudentColumn,
            string h16QualColumn,
            string h16IdColumn) => $@"
WITH Reconciliation AS (
    SELECT
        CAST(STUD.[{studStudentColumn}] AS nvarchar(255)) AS STUD_StudentNum,
        CAST(STUD.[{studQualColumn}] AS nvarchar(255)) AS STUD_QualCode,
        CAST(STUD.[{studIdColumn}] AS nvarchar(255)) AS STUD_IDNum,
        CAST(AUDIT.[{auditStudentColumn}] AS nvarchar(255)) AS AUDIT_StudentNum,
        CAST(AUDIT.[{auditQualColumn}] AS nvarchar(255)) AS AUDIT_QualCode,
        CAST(AUDIT.[{auditIdColumn}] AS nvarchar(255)) AS AUDIT_IDNum,
        CAST(H16.[{h16StudentColumn}] AS nvarchar(255)) AS H16_StudentNum,
        CAST(H16.[{h16QualColumn}] AS nvarchar(255)) AS H16_QualCode,
        CAST(H16.[{h16IdColumn}] AS nvarchar(255)) AS H16_IDNum,
        CASE
            WHEN H16.[{h16StudentColumn}] IS NULL THEN 'MISSING_IN_H16'
            WHEN STUD.[{studIdColumn}] <> H16.[{h16IdColumn}] THEN 'ID_MISMATCH_H16'
            WHEN STUD.[{studQualColumn}] <> H16.[{h16QualColumn}] THEN 'QUAL_MISMATCH_H16'
            WHEN AUDIT.[{auditStudentColumn}] IS NULL THEN 'MISSING_IN_AUDIT'
            WHEN STUD.[{studIdColumn}] <> AUDIT.[{auditIdColumn}] THEN 'ID_MISMATCH_AUDIT'
            WHEN STUD.[{studQualColumn}] <> AUDIT.[{auditQualColumn}] THEN 'QUAL_MISMATCH_AUDIT'
            ELSE 'MATCH'
        END AS Reconciliation_Status,
        CASE
            WHEN H16.[{h16StudentColumn}] IS NULL THEN 'H16 record missing'
            WHEN STUD.[{studIdColumn}] <> H16.[{h16IdColumn}] THEN 'ID mismatch with H16'
            WHEN STUD.[{studQualColumn}] <> H16.[{h16QualColumn}] THEN 'Qualification mismatch with H16'
            WHEN AUDIT.[{auditStudentColumn}] IS NULL THEN 'Audit record missing'
            WHEN STUD.[{studIdColumn}] <> AUDIT.[{auditIdColumn}] THEN 'ID mismatch with Audit'
            WHEN STUD.[{studQualColumn}] <> AUDIT.[{auditQualColumn}] THEN 'Qualification mismatch with Audit'
            ELSE 'All records match'
        END AS Issue_Description
    FROM [{studTable}] STUD
    LEFT JOIN [{auditTable}] AUDIT
        ON STUD.[{studStudentColumn}] = AUDIT.[{auditStudentColumn}]
    LEFT JOIN [{h16Table}] H16
        ON STUD.[{studStudentColumn}] = H16.[{h16StudentColumn}]
)";

        private static async Task<List<Rule23ReconciliationRowViewModel>> LoadReconciliationRowsAsync(
            SqlConnection connection,
            string reconciliationCte,
            string whereClause,
            int topLimit)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = $@"
{reconciliationCte}
SELECT TOP ({topLimit})
    STUD_StudentNum,
    STUD_QualCode,
    STUD_IDNum,
    AUDIT_StudentNum,
    AUDIT_QualCode,
    AUDIT_IDNum,
    H16_StudentNum,
    H16_QualCode,
    H16_IDNum,
    Reconciliation_Status,
    Issue_Description
FROM Reconciliation
{whereClause};";

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule23ReconciliationRowViewModel>();

            while (await reader.ReadAsync())
            {
                rows.Add(new Rule23ReconciliationRowViewModel
                {
                    ValidationNumber = rows.Count + 1,
                    StudStudentNumber = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    StudQualificationCode = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    StudIdNumber = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    AuditStudentNumber = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    AuditQualificationCode = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    AuditIdNumber = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    H16StudentNumber = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    H16QualificationCode = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    H16IdNumber = reader.IsDBNull(8) ? "" : reader.GetString(8),
                    ReconciliationStatus = reader.IsDBNull(9) ? "" : reader.GetString(9),
                    IssueDescription = reader.IsDBNull(10) ? "" : reader.GetString(10)
                });
            }

            return rows;
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        private static Rule23ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule23ValidationSummary>(decoded);
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyBrowserPreview(Rule23ValidationSummary summary)
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


