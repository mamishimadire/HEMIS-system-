using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.Helpers;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule26Service : IRule26Service
    {
        private const int RuleNumber = 26;
        private const string RuleName = "Rule 26 - dbo_PROF to Payroll_Sample 4-Control Validation";
        private const int BrowserPreviewRowLimit = 10;

        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule26Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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

        public async Task<TableListResult> GetTablesAsync(string server, string database, string driver)
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

                var profTable = tables.FirstOrDefault(t => t.Equals("dbo_PROF", StringComparison.OrdinalIgnoreCase))
                    ?? tables.FirstOrDefault(t => t.Equals("PROF", StringComparison.OrdinalIgnoreCase))
                    ?? tables.FirstOrDefault(t => t.Contains("prof", StringComparison.OrdinalIgnoreCase));
                var payrollTable = tables.FirstOrDefault(t => t.Equals("Payroll_Sample", StringComparison.OrdinalIgnoreCase))
                    ?? tables.FirstOrDefault(t => t.Equals("PAYROLL", StringComparison.OrdinalIgnoreCase))
                    ?? tables.FirstOrDefault(t => t.Contains("payroll", StringComparison.OrdinalIgnoreCase));

                return new TableListResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = profTable,
                    AutoDeceasedTable = payrollTable
                };
            }
            catch (Exception ex)
            {
                return new TableListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule26ColumnSelectionResult> GetColumnsAsync(string server, string database, string driver, string tableName, bool isProfTable)
        {
            try
            {
                var columns = await GetTableColumnsAsync(server, database, driver, tableName);

                return new Rule26ColumnSelectionResult
                {
                    Success = true,
                    Columns = columns,
                    AutoPersonnelColumn = isProfTable
                        ? FindFirst(columns, ["_037"], ["037", "personnel", "staff", "employee"])
                        : FindFirst(columns, ["PERSONNEL_NUMBER"], ["personnel_number", "personnel", "employee", "staff"]),
                    AutoEmploymentTypeColumn = isProfTable
                        ? FindFirst(columns, ["_041"], ["041", "perm", "temp", "employment"])
                        : FindFirst(columns, ["PERMANENT_OR_TEMP"], ["permanent_or_temp", "perm", "temp", "employment"]),
                    AutoGenderColumn = isProfTable
                        ? FindFirst(columns, ["_012"], ["012", "gender", "sex"])
                        : FindFirst(columns, ["GENDER"], ["gender", "sex"]),
                    AutoGroupColumn = isProfTable
                        ? FindFirst(columns, ["_013"], ["013", "group", "race"])
                        : FindFirst(columns, ["GROUP_NAME"], ["group_name", "group", "race"]),
                    AutoBirthDateColumn = isProfTable
                        ? FindFirst(columns, ["_011"], ["011", "birth", "dob", "date"])
                        : FindFirst(columns, ["BIRTH_DATE"], ["birth_date", "birth", "dob", "date"])
                };
            }
            catch (Exception ex)
            {
                return new Rule26ColumnSelectionResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule26VerifyResult> VerifyTablesAsync(Rule26VerifyRequest request)
        {
            try
            {
                ValidateVerifyRequest(request);

                var profTable = Sanitise(request.ProfTable);
                var payrollTable = Sanitise(request.PayrollTable);
                var profPersonnel = Sanitise(request.ProfPersonnelColumn);
                var payrollPersonnel = Sanitise(request.PayrollPersonnelColumn);
                var profKeySql = $"LTRIM(RTRIM(CAST([{profPersonnel}] AS nvarchar(4000))))";
                var payrollKeySql = $"LTRIM(RTRIM(CAST([{payrollPersonnel}] AS nvarchar(4000))))";

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var sql = $@"
WITH ProfBase AS
(
    SELECT {profKeySql} AS PersonnelKey
    FROM [{profTable}]
    WHERE [{profPersonnel}] IS NOT NULL
),
PayrollBase AS
(
    SELECT {payrollKeySql} AS PersonnelKey
    FROM [{payrollTable}]
)
SELECT
    (SELECT COUNT(*) FROM ProfBase) AS ProfCount,
    (SELECT COUNT(*) FROM PayrollBase) AS PayrollCount,
    (SELECT COUNT(*) FROM ProfBase p WHERE EXISTS (SELECT 1 FROM PayrollBase py WHERE py.PersonnelKey = p.PersonnelKey)) AS LinkedCount,
    (SELECT COUNT(*) FROM ProfBase p WHERE NOT EXISTS (SELECT 1 FROM PayrollBase py WHERE py.PersonnelKey = p.PersonnelKey)) AS ProfWithoutPayroll,
    (SELECT COUNT(*) FROM PayrollBase py WHERE NOT EXISTS (SELECT 1 FROM ProfBase p WHERE p.PersonnelKey = py.PersonnelKey)) AS PayrollWithoutProf;";

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = sql;
                await using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                    return new Rule26VerifyResult { Success = false, Error = "No verification data returned." };

                return new Rule26VerifyResult
                {
                    Success = true,
                    ProfRecordCount = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
                    PayrollRecordCount = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                    LinkedRecordCount = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2)),
                    ProfWithoutPayrollCount = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)),
                    PayrollWithoutProfCount = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4))
                };
            }
            catch (Exception ex)
            {
                return new Rule26VerifyResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule26ValidationSummary> RunValidationAsync(Rule26ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);

                var summary = await AnalyseAsync(request);
                if (summary.Success && request.ClientId > 0)
                {
                    try
                    {
                        summary.SavedRunId = await SaveValidationRunAsync(request, summary, userEmail, userName);
                    }
                    catch (Exception ex)
                    {
                        summary.Warning = $"Validation completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule26ValidationSummary
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

        public async Task<Rule26WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
    ISNULL(vr.StudTable, '') AS ProfTable,
    ISNULL(vr.DeceasedTable, '') AS PayrollTable,
    ISNULL(vr.StudColumn, '') AS ProfPersonnelColumn,
    ISNULL(vr.DeceasedColumn, '') AS PayrollPersonnelColumn,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 26
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

            var workspace = new Rule26WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ProfTable = deserializedSummary?.ProfTable ?? (reader.IsDBNull(4) ? "" : reader.GetString(4)),
                PayrollTable = deserializedSummary?.PayrollTable ?? (reader.IsDBNull(5) ? "" : reader.GetString(5)),
                ProfPersonnelColumn = deserializedSummary?.ProfPersonnelColumn ?? (reader.IsDBNull(6) ? "" : reader.GetString(6)),
                PayrollPersonnelColumn = deserializedSummary?.PayrollPersonnelColumn ?? (reader.IsDBNull(7) ? "" : reader.GetString(7)),
                ProfEmploymentTypeColumn = deserializedSummary?.ProfEmploymentTypeColumn ?? "",
                ProfGenderColumn = deserializedSummary?.ProfGenderColumn ?? "",
                ProfGroupColumn = deserializedSummary?.ProfGroupColumn ?? "",
                ProfBirthDateColumn = deserializedSummary?.ProfBirthDateColumn ?? "",
                BlankPayrollGroupPassCodes = deserializedSummary?.BlankPayrollGroupPassCodes ?? "Z",
                PayrollEmploymentTypeColumn = deserializedSummary?.PayrollEmploymentTypeColumn ?? "",
                PayrollGenderColumn = deserializedSummary?.PayrollGenderColumn ?? "",
                PayrollGroupColumn = deserializedSummary?.PayrollGroupColumn ?? "",
                PayrollBirthDateColumn = deserializedSummary?.PayrollBirthDateColumn ?? "",
                CurrentStatus = reader.IsDBNull(8) ? "" : reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9) ? null : reader.GetString(9),
                LastEditedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Summary = summary
            };

            await reader.CloseAsync();

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

            return workspace;
        }

        public async Task<Rule26RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 26;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            var viewModel = new Rule26RunReviewViewModel
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

        public async Task<Rule26WorkspaceSaveResult> SaveWorkspaceAsync(Rule26ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                {
                    return new Rule26WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run the validation first so the workspace can be saved."
                    };
                }

                ValidateRequest(request);

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                {
                    return new Rule26WorkspaceSaveResult
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
    StudTable = @ProfTable,
    DeceasedTable = @PayrollTable,
    StudColumn = @ProfPersonnelColumn,
    DeceasedColumn = @PayrollPersonnelColumn,
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
                command.Parameters.AddWithValue("@ProfTable", request.ProfTable);
                command.Parameters.AddWithValue("@PayrollTable", request.PayrollTable);
                command.Parameters.AddWithValue("@ProfPersonnelColumn", request.ProfPersonnelColumn);
                command.Parameters.AddWithValue("@PayrollPersonnelColumn", request.PayrollPersonnelColumn);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule26|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|{request.ProfTable}|{request.PayrollTable}|{request.ProfPersonnelColumn}|{request.PayrollPersonnelColumn}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule26WorkspaceSaveResult
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
                return new Rule26WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule26WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule26WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "The saved workspace could not be found."
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
    WorkspaceSavedAt = NULL,
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID;";
                command.Parameters.AddWithValue("@RunID", runId);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule26|{runId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule26WorkspaceSaveResult
                {
                    Success = true,
                    Message = clearedSignoffs > 0
                        ? "Workspace editing enabled. Existing signoffs were removed."
                        : "Workspace editing enabled.",
                    SignoffsCleared = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule26WorkspaceSaveResult
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

            var clientId = await GetClientIdForRunAsync(connection, runId);
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

        public Task<string> GenerateSqlAsync(Rule26ValidationRequest request)
        {
            ValidateRequest(request);

            var profTable = Sanitise(request.ProfTable);
            var payrollTable = Sanitise(request.PayrollTable);
            var profPersonnel = Sanitise(request.ProfPersonnelColumn);
            var profEmployment = Sanitise(request.ProfEmploymentTypeColumn);
            var profGender = Sanitise(request.ProfGenderColumn);
            var profGroup = Sanitise(request.ProfGroupColumn);
            var profBirth = Sanitise(request.ProfBirthDateColumn);
            var payrollPersonnel = Sanitise(request.PayrollPersonnelColumn);
            var payrollEmployment = Sanitise(request.PayrollEmploymentTypeColumn);
            var payrollGender = Sanitise(request.PayrollGenderColumn);
            var payrollGroup = Sanitise(request.PayrollGroupColumn);
            var payrollBirth = Sanitise(request.PayrollBirthDateColumn);
            var blankPayrollGroupPassCodeSqlList = BuildRule26SqlCharacterList(request.BlankPayrollGroupPassCodes);

            var profPersonnelExpr = $"LTRIM(RTRIM(CAST([{profPersonnel}] AS nvarchar(4000))))";
            var payrollPersonnelExpr = $"LTRIM(RTRIM(CAST([{payrollPersonnel}] AS nvarchar(4000))))";
            var profBirthExpr = BuildProfBirthSqlExpression($"LTRIM(RTRIM(CAST([{profBirth}] AS nvarchar(50))))");
            var payrollBirthExpr = $"TRY_CONVERT(date, [{payrollBirth}])";

            var sql = $@"-- ============================================================================
-- HEMIS RULE 26: DBO_PROF TO PAYROLL_SAMPLE 5-CONTROL VALIDATION
-- ============================================================================

IF OBJECT_ID('tempdb..#ProfBase')    IS NOT NULL DROP TABLE #ProfBase;
IF OBJECT_ID('tempdb..#PayrollBase') IS NOT NULL DROP TABLE #PayrollBase;
IF OBJECT_ID('tempdb..#Exceptions')  IS NOT NULL DROP TABLE #Exceptions;

-- ============================================================
-- BUILD BASE TABLES
-- ============================================================
SELECT
    {profPersonnelExpr}                                        AS PersonnelKey,
    {profPersonnelExpr}                                        AS PersonnelNumber,
    LTRIM(RTRIM(CAST([{profEmployment}] AS nvarchar(4000))))   AS EmploymentType,
    UPPER(LTRIM(RTRIM(CAST([{profGender}] AS nvarchar(100))))) AS GenderValue,
    LTRIM(RTRIM(CAST([{profGroup}] AS nvarchar(4000))))        AS GroupValue,
    LTRIM(RTRIM(CAST([{profBirth}] AS nvarchar(50))))          AS BirthRawValue,
    {profBirthExpr}                                            AS BirthDateValue
INTO #ProfBase
FROM [{profTable}]
WHERE [{profPersonnel}] IS NOT NULL;

SELECT
    {payrollPersonnelExpr}                                                  AS PersonnelKey,
    {payrollPersonnelExpr}                                                  AS PersonnelNumber,
    LTRIM(RTRIM(CAST([{payrollEmployment}] AS nvarchar(4000))))             AS EmploymentType,
    UPPER(LTRIM(RTRIM(CAST([{payrollGender}] AS nvarchar(100)))))           AS GenderValue,
    LTRIM(RTRIM(CAST([{payrollGroup}] AS nvarchar(4000))))                  AS GroupValue,
    LTRIM(RTRIM(CAST([{payrollBirth}] AS nvarchar(50))))                    AS BirthRawValue,
    {payrollBirthExpr}                                                      AS BirthDateValue
INTO #PayrollBase
FROM [{payrollTable}];

-- ============================================================
-- STEP 1: POPULATION COUNTS
-- ============================================================
SELECT 'PROF population'     AS Metric, COUNT(*) AS RecordCount FROM #ProfBase
UNION ALL
SELECT 'Payroll population',             COUNT(*) FROM #PayrollBase
UNION ALL
SELECT 'Linked population',              COUNT(*)
FROM #ProfBase p
WHERE EXISTS (SELECT 1 FROM #PayrollBase py WHERE py.PersonnelKey = p.PersonnelKey)
UNION ALL
SELECT 'PROF without Payroll',           COUNT(*)
FROM #ProfBase p
LEFT JOIN #PayrollBase py ON py.PersonnelKey = p.PersonnelKey
WHERE py.PersonnelKey IS NULL;

-- ============================================================
-- STEP 2: STAGE EXCEPTIONS
-- ============================================================
SELECT
    CAST('{profTable} -> {payrollTable}' AS nvarchar(100)) AS ControlDirection,
    CAST(2 AS int)                                          AS ControlNumber,
    CAST('Employment Type Match' AS nvarchar(100))          AS ControlName,
    CAST(p.PersonnelNumber AS nvarchar(255))                AS PersonnelNumber,
    CAST('{profTable} employment type first letter does not match {payrollTable} PERMANENT_OR_TEMP.'
                                          AS nvarchar(500)) AS ExceptionReason,
    CAST(p.EmploymentType  AS nvarchar(255))                AS PROF_Value,
    CAST(mismatch.EmploymentType AS nvarchar(255))          AS Payroll_Value
INTO #Exceptions
FROM #ProfBase p
OUTER APPLY (
    SELECT TOP 1 py.EmploymentType
    FROM #PayrollBase py
    WHERE py.PersonnelKey = p.PersonnelKey
      AND LEFT(p.EmploymentType, 1) <> LEFT(py.EmploymentType, 1)
) mismatch
WHERE EXISTS (SELECT 1 FROM #PayrollBase py WHERE py.PersonnelKey = p.PersonnelKey)
  AND NOT EXISTS (
      SELECT 1 FROM #PayrollBase py
      WHERE py.PersonnelKey = p.PersonnelKey
        AND LEFT(p.EmploymentType, 1) = LEFT(py.EmploymentType, 1)
  )

UNION ALL

-- CONTROL 3: Gender Consistency
SELECT
    '{profTable} -> {payrollTable}',
    3,
    'Gender Consistency',
    p.PersonnelNumber,
    '{profTable} gender first letter does not match {payrollTable} GENDER first letter.',
    p.GenderValue,
    mismatch.GenderValue
FROM #ProfBase p
OUTER APPLY (
    SELECT TOP 1 py.GenderValue
    FROM #PayrollBase py
    WHERE py.PersonnelKey = p.PersonnelKey
      AND LEFT(p.GenderValue, 1) <> LEFT(py.GenderValue, 1)
) mismatch
WHERE EXISTS (SELECT 1 FROM #PayrollBase py WHERE py.PersonnelKey = p.PersonnelKey)
  AND NOT EXISTS (
      SELECT 1 FROM #PayrollBase py
      WHERE py.PersonnelKey = p.PersonnelKey
        AND LEFT(p.GenderValue, 1) = LEFT(py.GenderValue, 1)
  )

UNION ALL

-- CONTROL 4: Race/Group Code Accuracy
SELECT
    '{profTable} -> {payrollTable}',
    4,
    'Race/Group Code Accuracy',
    p.PersonnelNumber,
    '{profTable} race/group code first letter does not match {payrollTable} GROUP_NAME first letter.',
    p.GroupValue,
    mismatch.GroupValue
FROM #ProfBase p
OUTER APPLY (
    SELECT TOP 1 py.GroupValue
    FROM #PayrollBase py
    WHERE py.PersonnelKey = p.PersonnelKey
      AND NOT (
          LEFT(p.GroupValue, 1) IN ({blankPayrollGroupPassCodeSqlList})
          AND NULLIF(LTRIM(RTRIM(py.GroupValue)), '') IS NULL
      )
      AND LEFT(p.GroupValue, 1) <> LEFT(py.GroupValue, 1)
) mismatch
WHERE EXISTS (SELECT 1 FROM #PayrollBase py WHERE py.PersonnelKey = p.PersonnelKey)
  AND NOT EXISTS (
      SELECT 1 FROM #PayrollBase py
      WHERE py.PersonnelKey = p.PersonnelKey
        AND (
            LEFT(p.GroupValue, 1) = LEFT(py.GroupValue, 1)
            OR (
                LEFT(p.GroupValue, 1) IN ({blankPayrollGroupPassCodeSqlList})
                AND NULLIF(LTRIM(RTRIM(py.GroupValue)), '') IS NULL
            )
        )
  )

UNION ALL

-- CONTROL 5a: Birth Date - invalid YYYYMMDD format
SELECT
    '{profTable} -> {payrollTable}',
    5,
    'Birth Date Integrity',
    p.PersonnelNumber,
    '{profTable} birth date is not a valid YYYYMMDD value and cannot be matched to {payrollTable} birth date.',
    p.BirthRawValue,
    COALESCE(CONVERT(nvarchar(30), sample.BirthDateValue, 23), sample.BirthRawValue, '')
FROM #ProfBase p
OUTER APPLY (
    SELECT TOP 1 py.BirthRawValue, py.BirthDateValue
    FROM #PayrollBase py
    WHERE py.PersonnelKey = p.PersonnelKey
    ORDER BY CASE WHEN py.BirthDateValue IS NULL THEN 1 ELSE 0 END, py.BirthRawValue
) sample
WHERE p.BirthRawValue <> ''
  AND p.BirthDateValue IS NULL
  AND EXISTS (SELECT 1 FROM #PayrollBase py WHERE py.PersonnelKey = p.PersonnelKey)

UNION ALL

-- CONTROL 5b: Birth Date - valid format but does not match
SELECT
    '{profTable} -> {payrollTable}',
    5,
    'Birth Date Integrity',
    p.PersonnelNumber,
    '{profTable} birth date does not match {payrollTable} birth date.',
    CONVERT(nvarchar(30), p.BirthDateValue, 23),
    CONVERT(nvarchar(30), mismatch.BirthDateValue, 23)
FROM #ProfBase p
OUTER APPLY (
    SELECT TOP 1 py.BirthDateValue
    FROM #PayrollBase py
    WHERE py.PersonnelKey = p.PersonnelKey
      AND (py.BirthDateValue IS NULL OR p.BirthDateValue <> py.BirthDateValue)
) mismatch
WHERE p.BirthDateValue IS NOT NULL
  AND EXISTS (SELECT 1 FROM #PayrollBase py WHERE py.PersonnelKey = p.PersonnelKey)
  AND NOT EXISTS (
      SELECT 1 FROM #PayrollBase py
      WHERE py.PersonnelKey = p.PersonnelKey
        AND py.BirthDateValue = p.BirthDateValue
  );

-- ============================================================
-- STEP 3: SUMMARY PER CONTROL
-- ============================================================
SELECT
    ControlNumber,
    ControlName,
    COUNT(*) AS Exception_Count
FROM #Exceptions
GROUP BY ControlNumber, ControlName
ORDER BY ControlNumber;

-- ============================================================
-- STEP 4: FULL EXCEPTION DETAIL
-- ============================================================
SELECT
    ControlDirection,
    ControlNumber,
    ControlName,
    PersonnelNumber,
    ExceptionReason,
    PROF_Value,
    Payroll_Value
FROM #Exceptions
ORDER BY ControlNumber, PersonnelNumber;

-- ============================================================
-- CLEANUP
-- ============================================================
DROP TABLE #ProfBase;
DROP TABLE #PayrollBase;
DROP TABLE #Exceptions;";

            return Task.FromResult(sql);
        }

        private async Task<Rule26ValidationSummary> AnalyseAsync(Rule26ValidationRequest request)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var payrollColumns = await GetTableColumnsAsync(request.Server, request.Database, request.Driver, request.PayrollTable);
            var payrollNameColumn = payrollColumns.FirstOrDefault(c => c.Equals("PERSONNEL_NAME", StringComparison.OrdinalIgnoreCase));

            var profRecords = await LoadProfRecordsAsync(conn, request);
            var payrollRecords = await LoadPayrollRecordsAsync(conn, request, payrollNameColumn);

            var payrollByPersonnel = payrollRecords
                .Where(r => !string.IsNullOrWhiteSpace(r.PersonnelKey))
                .GroupBy(r => r.PersonnelKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var direction1 = BuildProfToPayrollDirection(profRecords, payrollByPersonnel, request);

            var totalValidated = direction1.Controls.Sum(c => c.TotalTested);
            var totalExceptions = direction1.TotalExceptions;
            var linkedRecordCount = direction1.LinkedRecordCount;
            var passCount = Math.Max(0, totalValidated - totalExceptions);

            return new Rule26ValidationSummary
            {
                Success = true,
                TotalValidated = totalValidated,
                MatchingCount = linkedRecordCount,
                DisplayedCount = totalExceptions,
                PassCount = passCount,
                FailCount = totalExceptions,
                ExceptionRate = totalValidated == 0 ? 0 : Math.Round((decimal)totalExceptions / totalValidated * 100m, 2),
                Status = totalExceptions == 0 ? "PASS" : "FAIL",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                ProfTable = request.ProfTable,
                PayrollTable = request.PayrollTable,
                ProfPersonnelColumn = request.ProfPersonnelColumn,
                ProfEmploymentTypeColumn = request.ProfEmploymentTypeColumn,
                ProfGenderColumn = request.ProfGenderColumn,
                ProfGroupColumn = request.ProfGroupColumn,
                ProfBirthDateColumn = request.ProfBirthDateColumn,
                BlankPayrollGroupPassCodes = request.BlankPayrollGroupPassCodes,
                PayrollPersonnelColumn = request.PayrollPersonnelColumn,
                PayrollEmploymentTypeColumn = request.PayrollEmploymentTypeColumn,
                PayrollGenderColumn = request.PayrollGenderColumn,
                PayrollGroupColumn = request.PayrollGroupColumn,
                PayrollBirthDateColumn = request.PayrollBirthDateColumn,
                ProfRecordCount = profRecords.Count,
                PayrollRecordCount = payrollRecords.Count,
                LinkedRecordCount = linkedRecordCount,
                ClientId = request.ClientId,
                Directions = [direction1],
                Exceptions = direction1.Exceptions.ToList(),
                PassRows = direction1.PassRows.ToList()
            };
        }

        private async Task<List<ProfRecord>> LoadProfRecordsAsync(SqlConnection conn, Rule26ValidationRequest request)
        {
            var profTable = Sanitise(request.ProfTable);
            var personnel = Sanitise(request.ProfPersonnelColumn);
            var employment = Sanitise(request.ProfEmploymentTypeColumn);
            var gender = Sanitise(request.ProfGenderColumn);
            var group = Sanitise(request.ProfGroupColumn);
            var birth = Sanitise(request.ProfBirthDateColumn);

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = $@"
SELECT [{personnel}], [{employment}], [{gender}], [{group}], [{birth}]
FROM [{profTable}]
WHERE [{personnel}] IS NOT NULL;";

            await using var reader = await cmd.ExecuteReaderAsync();
            var records = new List<ProfRecord>();
            while (await reader.ReadAsync())
            {
                var rawPersonnel = ToInvariantString(reader.GetValue(0));
                records.Add(new ProfRecord
                {
                    PersonnelNumber = rawPersonnel,
                    PersonnelKey = rawPersonnel,
                    EmploymentType = ToNullableInvariantString(reader.GetValue(1)),
                    Gender = ToNullableInvariantString(reader.GetValue(2)),
                    GroupCode = ToNullableInvariantString(reader.GetValue(3)),
                    BirthRaw = ToNullableInvariantString(reader.GetValue(4)),
                    BirthDate = ConvertProfBirthDate(ToNullableInvariantString(reader.GetValue(4)))
                });
            }

            return records;
        }

        private async Task<List<PayrollRecord>> LoadPayrollRecordsAsync(SqlConnection conn, Rule26ValidationRequest request, string? payrollNameColumn)
        {
            var payrollTable = Sanitise(request.PayrollTable);
            var personnel = Sanitise(request.PayrollPersonnelColumn);
            var employment = Sanitise(request.PayrollEmploymentTypeColumn);
            var gender = Sanitise(request.PayrollGenderColumn);
            var group = Sanitise(request.PayrollGroupColumn);
            var birth = Sanitise(request.PayrollBirthDateColumn);
            var selectName = string.IsNullOrWhiteSpace(payrollNameColumn)
                ? "CAST(NULL AS nvarchar(4000))"
                : $"[{Sanitise(payrollNameColumn)}]";

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = $@"
SELECT [{personnel}], {selectName}, [{employment}], [{gender}], [{group}], [{birth}]
FROM [{payrollTable}];";

            await using var reader = await cmd.ExecuteReaderAsync();
            var records = new List<PayrollRecord>();
            while (await reader.ReadAsync())
            {
                var rawPersonnel = ToInvariantString(reader.GetValue(0));
                records.Add(new PayrollRecord
                {
                    PersonnelNumber = rawPersonnel,
                    PersonnelKey = rawPersonnel,
                    PersonnelName = ToNullableInvariantString(reader.GetValue(1)),
                    EmploymentType = ToNullableInvariantString(reader.GetValue(2)),
                    Gender = ToNullableInvariantString(reader.GetValue(3)),
                    GroupName = ToNullableInvariantString(reader.GetValue(4)),
                    BirthRaw = ToNullableInvariantString(reader.GetValue(5)),
                    BirthDate = NormalizeDate(ToNullableInvariantString(reader.GetValue(5)))
                });
            }

            return records;
        }

        private Rule26DirectionResultViewModel BuildProfToPayrollDirection(
            List<ProfRecord> profRecords,
            Dictionary<string, List<PayrollRecord>> payrollByPersonnel,
            Rule26ValidationRequest request)
        {
            var blankPayrollGroupPassCodes = ParseRule26BlankPayrollGroupPassCodes(request.BlankPayrollGroupPassCodes);
            var direction = new Rule26DirectionResultViewModel
            {
                DirectionKey = "prof_to_payroll",
                DirectionLabel = "dbo_PROF -> Payroll_Sample",
                BaseTable = request.ProfTable,
                ReferenceTable = request.PayrollTable,
                BaseRecordCount = profRecords.Count
            };

            var controls = CreateControlShell();
            var linkedGroups = new List<Rule26LinkedPayrollGroup>();

            foreach (var prof in profRecords)
            {
                if (!string.IsNullOrWhiteSpace(prof.PersonnelKey) &&
                    payrollByPersonnel.TryGetValue(prof.PersonnelKey, out var payrollMatches) &&
                    payrollMatches.Count > 0)
                {
                    linkedGroups.Add(new Rule26LinkedPayrollGroup
                    {
                        Prof = prof,
                        PayrollMatches = payrollMatches
                    });
                }
            }

            direction.LinkedRecordCount = linkedGroups.Count;

            EvaluateLinkedControlsForProfDirection(direction, controls, linkedGroups, blankPayrollGroupPassCodes);
            direction.Controls = controls;
            direction.TotalExceptions = direction.Exceptions.Count;

            var exceptionPersonnelProf = direction.Exceptions
                .Select(e => e.PersonnelNumber)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            direction.PassRows = linkedGroups
                .Where(group => !exceptionPersonnelProf.Contains(group.Prof.PersonnelNumber))
                .GroupBy(group => group.Prof.PersonnelNumber, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(group => new Rule26PassRowViewModel
                {
                    DirectionKey = direction.DirectionKey,
                    DirectionLabel = direction.DirectionLabel,
                    PersonnelNumber = group.Prof.PersonnelNumber,
                    PersonnelName = group.PayrollMatches
                        .Select(payroll => payroll.PersonnelName)
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
                    EmploymentType = group.Prof.EmploymentType,
                    Gender = group.Prof.Gender
                })
                .ToList();

            return direction;
        }

        private static List<Rule26ControlSummaryViewModel> CreateControlShell() =>
        [
            new()
            {
                ControlNumber = 2,
                ControlName = "Employment Type Match",
                Explanation = "Compares the first letter of dbo_PROF _041 to the first letter of Payroll PERMANENT_OR_TEMP for linked personnel."
            },
            new()
            {
                ControlNumber = 3,
                ControlName = "Gender Consistency",
                Explanation = "Compares the first letter of dbo_PROF _012 to the first letter of Payroll GENDER for linked personnel."
            },
            new()
            {
                ControlNumber = 4,
                ControlName = "Race/Group Code Accuracy",
                Explanation = "Compares the first letter of Payroll GROUP_NAME to the first letter of dbo_PROF _013 for linked personnel. Blank Payroll GROUP_NAME is accepted when dbo_PROF _013 is in the configured blank-group pass list."
            },
            new()
            {
                ControlNumber = 5,
                ControlName = "Birth Date Integrity",
                Explanation = "Compares Payroll BIRTH_DATE to dbo_PROF _011 converted from YYYYMMDD for linked personnel."
            }
        ];

        private void EvaluateLinkedControlsForProfDirection(
            Rule26DirectionResultViewModel direction,
            List<Rule26ControlSummaryViewModel> controls,
            List<Rule26LinkedPayrollGroup> linkedGroups,
            HashSet<string> blankPayrollGroupPassCodes)
        {
            foreach (var group in linkedGroups)
            {
                var employmentMismatch = GetRule26FailingPayrollMatch(
                    group.PayrollMatches,
                    payroll => AreSqlComparableValuesDifferent(
                        GetFirstCharacterValue(group.Prof.EmploymentType),
                        GetFirstCharacterValue(payroll.EmploymentType)));
                if (employmentMismatch != null)
                {
                    direction.Exceptions.Add(CreateException(
                        direction, controls[0], group.Prof.PersonnelNumber, employmentMismatch.PersonnelName,
                        "dbo_PROF employment type first letter does not match Payroll_Sample PERMANENT_OR_TEMP first letter.",
                        group.Prof.EmploymentType, employmentMismatch.EmploymentType));
                }

                var genderMismatch = GetRule26FailingPayrollMatch(
                    group.PayrollMatches,
                    payroll => AreSqlComparableValuesDifferent(
                        GetFirstCharacterValue(group.Prof.Gender),
                        GetFirstCharacterValue(payroll.Gender)));
                if (genderMismatch != null)
                {
                    direction.Exceptions.Add(CreateException(
                        direction, controls[1], group.Prof.PersonnelNumber, genderMismatch.PersonnelName,
                        "dbo_PROF gender first letter does not match Payroll_Sample GENDER first letter.",
                        group.Prof.Gender, genderMismatch.Gender));
                }

                var groupMismatch = GetRule26FailingPayrollMatch(
                    group.PayrollMatches,
                    payroll => IsRule26GroupMismatch(group.Prof.GroupCode, payroll.GroupName, blankPayrollGroupPassCodes));
                if (groupMismatch != null)
                {
                    direction.Exceptions.Add(CreateException(
                        direction, controls[2], group.Prof.PersonnelNumber, groupMismatch.PersonnelName,
                        "dbo_PROF race/group code first letter does not match Payroll_Sample GROUP_NAME first letter.",
                        group.Prof.GroupCode, groupMismatch.GroupName));
                }

                if (!string.IsNullOrWhiteSpace(group.Prof.BirthRaw) && !group.Prof.BirthDate.HasValue)
                {
                    var payrollBirthSample = GetPreferredRule26PayrollBirthSample(group.PayrollMatches);
                    direction.Exceptions.Add(CreateException(
                        direction, controls[3], group.Prof.PersonnelNumber, payrollBirthSample?.PersonnelName,
                        "dbo_PROF birth date is not a valid YYYYMMDD value and cannot be matched to Payroll_Sample birth date.",
                        group.Prof.BirthRaw,
                        FormatRule26BirthDisplay(payrollBirthSample)));
                }
                else if (group.Prof.BirthDate.HasValue)
                {
                    var birthMismatch = GetRule26FailingPayrollMatch(
                        group.PayrollMatches,
                        payroll => !payroll.BirthDate.HasValue || group.Prof.BirthDate.Value != payroll.BirthDate.Value);
                    if (birthMismatch != null)
                    {
                        direction.Exceptions.Add(CreateException(
                            direction, controls[3], group.Prof.PersonnelNumber, birthMismatch.PersonnelName,
                            "dbo_PROF birth date does not match Payroll_Sample birth date.",
                            group.Prof.BirthDate.Value.ToString("yyyy-MM-dd"), birthMismatch.BirthDate?.ToString("yyyy-MM-dd") ?? ""));
                    }
                }
            }

            direction.Exceptions = direction.Exceptions
                .GroupBy(e => $"{e.DirectionKey}|{e.ControlNumber}|{e.PersonnelNumber}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            for (var i = 0; i < controls.Count; i++)
            {
                controls[i].TotalTested = direction.LinkedRecordCount;
                controls[i].ExceptionCount = direction.Exceptions.Count(e => e.ControlNumber == controls[i].ControlNumber);
                controls[i].Passed = controls[i].ExceptionCount == 0;
            }
        }

        private static PayrollRecord? GetRule26FailingPayrollMatch(
            IEnumerable<PayrollRecord> payrollMatches,
            Func<PayrollRecord, bool> isMismatch)
        {
            PayrollRecord? firstMismatch = null;
            foreach (var payroll in payrollMatches)
            {
                if (!isMismatch(payroll))
                    return null;

                firstMismatch ??= payroll;
            }

            return firstMismatch;
        }

        private static PayrollRecord? GetPreferredRule26PayrollBirthSample(IEnumerable<PayrollRecord> payrollMatches) =>
            payrollMatches
                .OrderBy(payroll => payroll.BirthDate.HasValue ? 0 : 1)
                .ThenBy(payroll => payroll.BirthRaw)
                .FirstOrDefault();

        private static bool IsRule26GroupMismatch(
            string? profGroupCode,
            string? payrollGroupName,
            HashSet<string> blankPayrollGroupPassCodes)
        {
            var profGroupFirst = GetFirstCharacterValue(profGroupCode);
            var payrollGroupFirst = GetFirstCharacterValue(payrollGroupName);
            var payrollGroupBlank = string.IsNullOrEmpty(NormalizeText(payrollGroupName));

            if (!string.IsNullOrEmpty(profGroupFirst) &&
                blankPayrollGroupPassCodes.Contains(profGroupFirst) &&
                payrollGroupBlank)
                return false;

            return AreSqlComparableValuesDifferent(profGroupFirst, payrollGroupFirst);
        }

        private static string NormalizeRule26BlankPayrollGroupPassCodes(string? value)
        {
            var codes = ParseRule26BlankPayrollGroupPassCodes(value);
            return codes.Count == 0 ? "Z" : string.Join(",", codes.OrderBy(code => code, StringComparer.OrdinalIgnoreCase));
        }

        private static HashSet<string> ParseRule26BlankPayrollGroupPassCodes(string? value)
        {
            var tokens = (value ?? "Z")
                .Split([',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(GetFirstCharacterValue)
                .Where(token => !string.IsNullOrEmpty(token))
                .Select(token => token!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (tokens.Count == 0)
                tokens.Add("Z");

            return tokens;
        }

        private static string BuildRule26SqlCharacterList(string? value) =>
            string.Join(", ", ParseRule26BlankPayrollGroupPassCodes(value)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .Select(code => $"'{EscapeSqlString(code)}'"));

        private static string FormatRule26BirthDisplay(PayrollRecord? payroll) =>
            payroll?.BirthDate?.ToString("yyyy-MM-dd")
            ?? payroll?.BirthRaw
            ?? "";

        private static Rule26ExceptionRowViewModel CreateException(
            Rule26DirectionResultViewModel direction,
            Rule26ControlSummaryViewModel control,
            string personnelNumber,
            string? personnelName,
            string reason,
            string? baseValue,
            string? referenceValue)
        {
            var row = new Rule26ExceptionRowViewModel
            {
                DirectionKey = direction.DirectionKey,
                DirectionLabel = direction.DirectionLabel,
                ControlNumber = control.ControlNumber,
                ControlName = control.ControlName,
                PersonnelNumber = personnelNumber ?? "",
                PersonnelName = personnelName,
                ExceptionReason = reason,
                BaseValue = baseValue ?? "",
                ReferenceValue = referenceValue ?? ""
            };

            row.DisplayValues["Direction"] = row.DirectionLabel;
            row.DisplayValues["Control"] = row.ControlName;
            row.DisplayValues["Personnel Number"] = row.PersonnelNumber;
            row.DisplayValues["Personnel Name"] = row.PersonnelName;
            row.DisplayValues["Exception Reason"] = row.ExceptionReason;
            row.DisplayValues["Base Value"] = row.BaseValue;
            row.DisplayValues["Reference Value"] = row.ReferenceValue;
            return row;
        }

        private async Task<int> SaveValidationRunAsync(Rule26ValidationRequest request, Rule26ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, RuleNumber);

            var userId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!userId.HasValue)
                throw new InvalidOperationException("The data analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, RuleNumber);

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
    @ClientID, @UserID, @RuleNumber, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @ProfTable, @PayrollTable, @ProfPersonnelColumn, @PayrollPersonnelColumn,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", userId.Value);
            command.Parameters.AddWithValue("@RuleNumber", RuleNumber);
            command.Parameters.AddWithValue("@RuleName", RuleName);
            command.Parameters.AddWithValue("@Status", "Needs Review");
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@ProfTable", request.ProfTable);
            command.Parameters.AddWithValue("@PayrollTable", request.PayrollTable);
            command.Parameters.AddWithValue("@ProfPersonnelColumn", request.ProfPersonnelColumn);
            command.Parameters.AddWithValue("@PayrollPersonnelColumn", request.PayrollPersonnelColumn);
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary.Exceptions)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            var recordHash = ComputeHash($@"ValidationRun|Rule26|{runId}|{request.ClientId}|{request.Database}|{request.ProfTable}|{request.PayrollTable}|{summary.TotalValidated}|{summary.FailCount}|{summary.Status}|{DateTime.UtcNow:o}|{previousHash}");

            await using var updateCommand = connection.CreateConfiguredCommand();
            updateCommand.CommandText = @"
UPDATE dbo.ValidationRuns
SET RecordHash = @RecordHash
WHERE RunID = @RunID;";
            updateCommand.Parameters.AddWithValue("@RunID", runId);
            updateCommand.Parameters.AddWithValue("@RecordHash", recordHash);
            await updateCommand.ExecuteNonQueryAsync();

            return runId;
        }

        private static void ApplyRequestToSummary(Rule26ValidationSummary summary, Rule26ValidationRequest request)
        {
            summary.ClientId = request.ClientId;
            summary.Database = request.Database;
            summary.ProfTable = request.ProfTable;
            summary.PayrollTable = request.PayrollTable;
            summary.ProfPersonnelColumn = request.ProfPersonnelColumn;
            summary.ProfEmploymentTypeColumn = request.ProfEmploymentTypeColumn;
            summary.ProfGenderColumn = request.ProfGenderColumn;
            summary.ProfGroupColumn = request.ProfGroupColumn;
            summary.ProfBirthDateColumn = request.ProfBirthDateColumn;
            summary.BlankPayrollGroupPassCodes = request.BlankPayrollGroupPassCodes;
            summary.PayrollPersonnelColumn = request.PayrollPersonnelColumn;
            summary.PayrollEmploymentTypeColumn = request.PayrollEmploymentTypeColumn;
            summary.PayrollGenderColumn = request.PayrollGenderColumn;
            summary.PayrollGroupColumn = request.PayrollGroupColumn;
            summary.PayrollBirthDateColumn = request.PayrollBirthDateColumn;
        }

        private static void ValidateVerifyRequest(Rule26VerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.ProfTable) || string.IsNullOrWhiteSpace(request.PayrollTable))
                throw new InvalidOperationException("Both source tables are required.");
            if (string.IsNullOrWhiteSpace(request.ProfPersonnelColumn) || string.IsNullOrWhiteSpace(request.PayrollPersonnelColumn))
                throw new InvalidOperationException("Both personnel columns are required.");

            ValidateObjectName(request.ProfTable);
            ValidateObjectName(request.PayrollTable);
            ValidateObjectName(request.ProfPersonnelColumn);
            ValidateObjectName(request.PayrollPersonnelColumn);
        }

        private static void ValidateRequest(Rule26ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");

            request.BlankPayrollGroupPassCodes = NormalizeRule26BlankPayrollGroupPassCodes(request.BlankPayrollGroupPassCodes);

            var values = new[]
            {
                request.ProfTable, request.PayrollTable,
                request.ProfPersonnelColumn, request.ProfEmploymentTypeColumn, request.ProfGenderColumn, request.ProfGroupColumn, request.ProfBirthDateColumn,
                request.PayrollPersonnelColumn, request.PayrollEmploymentTypeColumn, request.PayrollGenderColumn, request.PayrollGroupColumn, request.PayrollBirthDateColumn
            };

            if (values.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Select both tables and all ten required columns before running Rule 26.");

            foreach (var value in values)
                ValidateObjectName(value!);
        }

        private static void ValidateObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Contains(";") ||
                value.Contains("--") ||
                value.Contains("/*") ||
                value.Contains("*/"))
            {
                throw new InvalidOperationException("An invalid table or column name was supplied.");
            }
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
            cmd.Parameters.AddWithValue("@TableName", tableName);

            await using var reader = await cmd.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(0));

            return columns;
        }

        private static string? FindFirst(IEnumerable<string> columns, IEnumerable<string> exactMatches, IEnumerable<string> partialMatches)
        {
            var list = columns.ToList();
            var exact = exactMatches
                .Select(match => list.FirstOrDefault(c => c.Equals(match, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

            if (!string.IsNullOrWhiteSpace(exact))
                return exact;

            return list.FirstOrDefault(c =>
            {
                var normalized = c.Trim().ToLowerInvariant();
                return partialMatches.Any(match => normalized.Contains(match.ToLowerInvariant()));
            }) ?? list.FirstOrDefault();
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

        private async Task<Rule26ValidationSummary?> GetValidationSummaryAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ResultsJSON FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value
                ? null
                : DeserializeSummary(Convert.ToString(value));
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

        private static string EscapeSqlString(string value) =>
            value.Replace("'", "''");

        private static string ToInvariantString(object? value)
        {
            if (value == null || value == DBNull.Value)
                return string.Empty;

            return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        }

        private static string? ToNullableInvariantString(object? value)
        {
            if (value == null || value == DBNull.Value)
                return null;

            return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        }

        private static string NormalizePersonnel(string? value) =>
            NumericFilterValueHelper.NormalizeNumericLikeValue(value);

        private static string? NormalizeText(string? value)
        {
            if (value == null)
                return null;

            return value.Trim().ToUpperInvariant();
        }

        private static string? GetFirstCharacterValue(string? value)
        {
            var normalized = NormalizeText(value);
            return string.IsNullOrEmpty(normalized) ? string.Empty : normalized[..1];
        }

        private static bool AreSqlComparableValuesDifferent(string? left, string? right)
        {
            if (left == null || right == null)
                return false;

            return !string.Equals(left, right, StringComparison.Ordinal);
        }

        private static DateTime? ConvertProfBirthDate(string? value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length != 8 || !trimmed.All(char.IsDigit))
                return null;

            return DateTime.TryParseExact(trimmed, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt.Date
                : null;
        }

        private static DateTime? NormalizeDate(string? value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return null;

            return DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt.Date
                : null;
        }

        private static string BuildProfBirthSqlExpression(string trimmedExpression) =>
            $@"CASE
    WHEN {trimmedExpression} NOT LIKE '[1-2][0-9][0-9][0-9][0-1][0-9][0-3][0-9]' THEN NULL
    ELSE TRY_CONVERT(date, STUFF(STUFF({trimmedExpression}, 5, 0, '-'), 8, 0, '-'))
END";

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        private static Rule26ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule26ValidationSummary>(decoded);
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyBrowserPreview(Rule26ValidationSummary summary)
        {
            summary.Exceptions = summary.Exceptions
                .Take(BrowserPreviewRowLimit)
                .ToList();
            summary.PassRows = summary.PassRows
                .Take(BrowserPreviewRowLimit)
                .ToList();
            summary.Directions = summary.Directions
                .Select(direction => new Rule26DirectionResultViewModel
                {
                    DirectionKey = direction.DirectionKey,
                    DirectionLabel = direction.DirectionLabel,
                    BaseTable = direction.BaseTable,
                    ReferenceTable = direction.ReferenceTable,
                    BaseRecordCount = direction.BaseRecordCount,
                    LinkedRecordCount = direction.LinkedRecordCount,
                    TotalExceptions = direction.TotalExceptions,
                    Controls = direction.Controls,
                    Exceptions = direction.Exceptions
                        .Take(BrowserPreviewRowLimit)
                        .ToList(),
                    PassRows = direction.PassRows
                        .Take(BrowserPreviewRowLimit)
                        .ToList()
                })
                .ToList();
            summary.DisplayedCount = Math.Min(summary.DisplayedCount, BrowserPreviewRowLimit);
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

        private sealed class ProfRecord
        {
            public string PersonnelNumber { get; set; } = "";
            public string PersonnelKey { get; set; } = "";
            public string? EmploymentType { get; set; }
            public string? Gender { get; set; }
            public string? GroupCode { get; set; }
            public string? BirthRaw { get; set; }
            public DateTime? BirthDate { get; set; }
        }

        private sealed class PayrollRecord
        {
            public string PersonnelNumber { get; set; } = "";
            public string PersonnelKey { get; set; } = "";
            public string? PersonnelName { get; set; }
            public string? EmploymentType { get; set; }
            public string? Gender { get; set; }
            public string? GroupName { get; set; }
            public string? BirthRaw { get; set; }
            public DateTime? BirthDate { get; set; }
        }

        private sealed class Rule26LinkedPayrollGroup
        {
            public ProfRecord Prof { get; set; } = new();
            public List<PayrollRecord> PayrollMatches { get; set; } = new();
        }
    }
}


