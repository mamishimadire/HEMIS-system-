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
        private const string RuleName = "Rule 26 - Complete 5-Control Bi-Directional Validation";

        private readonly IConfiguration _configuration;

        public Rule26Service(IConfiguration configuration)
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

                await using var cmd = conn.CreateCommand();
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

                await using var cmd = conn.CreateCommand();
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
    (SELECT COUNT(*) FROM ProfBase p INNER JOIN PayrollBase py ON p.PersonnelKey = py.PersonnelKey) AS LinkedCount,
    (SELECT COUNT(*) FROM ProfBase p WHERE NOT EXISTS (SELECT 1 FROM PayrollBase py WHERE py.PersonnelKey = p.PersonnelKey)) AS ProfWithoutPayroll,
    (SELECT COUNT(*) FROM PayrollBase py WHERE NOT EXISTS (SELECT 1 FROM ProfBase p WHERE p.PersonnelKey = py.PersonnelKey)) AS PayrollWithoutProf;";

                await using var cmd = conn.CreateCommand();
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

            await using var command = connection.CreateCommand();
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
            workspace.CurrentUserHasSignedOff = signoffs.Any(s => s.IsCurrentUser);
            workspace.CurrentUserSignoffComment = signoffs.FirstOrDefault(s => s.IsCurrentUser)?.Comment ?? "";

            return workspace;
        }

        public async Task<Rule26RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateCommand();
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
                var existingSummary = await GetValidationSummaryAsync(connection, request.RunId.Value);
                if (existingSummary != null)
                {
                    ApplyRequestToSummary(existingSummary, request);
                    existingSummary.SavedRunId = request.RunId.Value;
                }

                await using var command = connection.CreateCommand();
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
    ResultsJSON = @ResultsJSON,
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
                command.Parameters.AddWithValue("@ResultsJSON", existingSummary == null
                    ? DBNull.Value
                    : JsonConvert.SerializeObject(existingSummary));
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

                await using var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
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

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(engagementRole))
                throw new InvalidOperationException("Only assigned data analysts, managers, and directors can sign off a validation run.");

            if (!string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) &&
                !await HasSignoffRoleAsync(connection, runId, "DataAnalyst"))
            {
                throw new InvalidOperationException("The assigned data analyst must sign off before this review can be completed.");
            }

            await using var command = connection.CreateCommand();
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
            if (!CanSignOffAsRole(engagementRole))
                throw new InvalidOperationException("This user does not have a removable signoff role for the engagement.");

            await using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM dbo.ReviewSignoffs
WHERE RunID = @RunID
  AND ReviewerID = @ReviewerID
  AND SignoffRole = @SignoffRole;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ReviewerID", reviewerId.Value);
            command.Parameters.AddWithValue("@SignoffRole", engagementRole!);
            await command.ExecuteNonQueryAsync();

            await SetRunStatusAsync(connection, runId, "Needs Review");

            if (string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                await SetRunCurrentStateAsync(connection, runId, false);
            }
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

            var profPersonnelExpr = $"LTRIM(RTRIM(CAST([{profPersonnel}] AS nvarchar(4000))))";
            var payrollPersonnelExpr = $"LTRIM(RTRIM(CAST([{payrollPersonnel}] AS nvarchar(4000))))";
            var profBirthExpr = BuildProfBirthSqlExpression($"LTRIM(RTRIM(CAST([{profBirth}] AS nvarchar(50))))");
            var payrollBirthExpr = $"TRY_CONVERT(date, [{payrollBirth}])";

            var sql = $@"-- ============================================================================
-- HEMIS RULE 26: COMPLETE 5-CONTROL BI-DIRECTIONAL VALIDATION
-- ============================================================================
-- dbo_PROF <-> Payroll_Sample
-- This script tests BOTH directions in one execution and explains exceptions
-- according to the direction and control being tested.
-- ============================================================================

IF OBJECT_ID('tempdb..#ProfBase') IS NOT NULL DROP TABLE #ProfBase;
IF OBJECT_ID('tempdb..#PayrollBase') IS NOT NULL DROP TABLE #PayrollBase;

SELECT
    {profPersonnelExpr} AS PersonnelKey,
    LTRIM(RTRIM(CAST([{profPersonnel}] AS nvarchar(4000)))) AS PersonnelNumber,
    LTRIM(RTRIM(CAST([{profEmployment}] AS nvarchar(4000)))) AS EmploymentType,
    UPPER(LTRIM(RTRIM(CAST([{profGender}] AS nvarchar(100))))) AS GenderValue,
    LTRIM(RTRIM(CAST([{profGroup}] AS nvarchar(4000)))) AS GroupValue,
    {profBirthExpr} AS BirthDateValue
INTO #ProfBase
FROM [{profTable}]
WHERE [{profPersonnel}] IS NOT NULL;

SELECT
    {payrollPersonnelExpr} AS PersonnelKey,
    LTRIM(RTRIM(CAST([{payrollPersonnel}] AS nvarchar(4000)))) AS PersonnelNumber,
    LTRIM(RTRIM(CAST([{payrollEmployment}] AS nvarchar(4000)))) AS EmploymentType,
    UPPER(LTRIM(RTRIM(CAST([{payrollGender}] AS nvarchar(100))))) AS GenderValue,
    LTRIM(RTRIM(CAST([{payrollGroup}] AS nvarchar(4000)))) AS GroupValue,
    {payrollBirthExpr} AS BirthDateValue
INTO #PayrollBase
FROM [{payrollTable}];

-- STEP 1: POPULATION COUNTS
SELECT 'PROF population' AS Metric, COUNT(*) AS RecordCount FROM #ProfBase
UNION ALL
SELECT 'Payroll population', COUNT(*) FROM #PayrollBase
UNION ALL
SELECT 'Linked population', COUNT(*) FROM #ProfBase p INNER JOIN #PayrollBase py ON py.PersonnelKey = p.PersonnelKey;

-- STEP 2: COMBINED EXCEPTIONS WITH DIRECTION-SPECIFIC EXPLANATIONS
SELECT *
FROM
(
    SELECT
        'dbo_PROF -> Payroll_Sample' AS DirectionLabel,
        1 AS ControlNumber,
        'Personnel Number Existence' AS ControlName,
        p.PersonnelNumber,
        'Personnel number exists in dbo_PROF but not in Payroll_Sample.' AS ExceptionReason,
        p.PersonnelNumber AS BaseValue,
        '' AS ReferenceValue
    FROM #ProfBase p
    LEFT JOIN #PayrollBase py ON py.PersonnelKey = p.PersonnelKey
    WHERE py.PersonnelKey IS NULL

    UNION ALL

    SELECT
        'dbo_PROF -> Payroll_Sample',
        2,
        'Employment Type Match',
        p.PersonnelNumber,
        'dbo_PROF employment type first letter does not match Payroll_Sample PERMANENT_OR_TEMP.',
        p.EmploymentType,
        py.EmploymentType
    FROM #ProfBase p
    INNER JOIN #PayrollBase py ON py.PersonnelKey = p.PersonnelKey
    WHERE LEFT(p.EmploymentType, 1) <> py.EmploymentType

    UNION ALL

    SELECT
        'dbo_PROF -> Payroll_Sample',
        3,
        'Gender Consistency',
        p.PersonnelNumber,
        'dbo_PROF gender does not match Payroll_Sample gender.',
        p.GenderValue,
        py.GenderValue
    FROM #ProfBase p
    INNER JOIN #PayrollBase py ON py.PersonnelKey = p.PersonnelKey
    WHERE p.GenderValue <> py.GenderValue

    UNION ALL

    SELECT
        'dbo_PROF -> Payroll_Sample',
        4,
        'Race/Group Code Accuracy',
        p.PersonnelNumber,
        'dbo_PROF race/group code first letter does not match Payroll_Sample GROUP_NAME first letter.',
        p.GroupValue,
        py.GroupValue
    FROM #ProfBase p
    INNER JOIN #PayrollBase py ON py.PersonnelKey = p.PersonnelKey
    WHERE LEFT(p.GroupValue, 1) <> LEFT(py.GroupValue, 1)

    UNION ALL

    SELECT
        'dbo_PROF -> Payroll_Sample',
        5,
        'Birth Date Integrity',
        p.PersonnelNumber,
        'dbo_PROF birth date does not match Payroll_Sample birth date.',
        CONVERT(nvarchar(30), p.BirthDateValue, 23),
        CONVERT(nvarchar(30), py.BirthDateValue, 23)
    FROM #ProfBase p
    INNER JOIN #PayrollBase py ON py.PersonnelKey = p.PersonnelKey
    WHERE p.BirthDateValue IS NOT NULL
      AND (py.BirthDateValue IS NULL OR p.BirthDateValue <> py.BirthDateValue)

    UNION ALL

    SELECT
        'Payroll_Sample -> dbo_PROF',
        1,
        'Personnel Number Existence',
        py.PersonnelNumber,
        'Personnel number exists in Payroll_Sample but not in dbo_PROF.',
        py.PersonnelNumber,
        ''
    FROM #PayrollBase py
    LEFT JOIN #ProfBase p ON p.PersonnelKey = py.PersonnelKey
    WHERE p.PersonnelKey IS NULL

    UNION ALL

    SELECT
        'Payroll_Sample -> dbo_PROF',
        2,
        'Employment Type Match',
        py.PersonnelNumber,
        'Payroll_Sample employment type does not match the first letter of dbo_PROF employment type.',
        py.EmploymentType,
        p.EmploymentType
    FROM #PayrollBase py
    INNER JOIN #ProfBase p ON p.PersonnelKey = py.PersonnelKey
    WHERE py.EmploymentType <> LEFT(p.EmploymentType, 1)

    UNION ALL

    SELECT
        'Payroll_Sample -> dbo_PROF',
        3,
        'Gender Consistency',
        py.PersonnelNumber,
        'Payroll_Sample gender does not match dbo_PROF gender.',
        py.GenderValue,
        p.GenderValue
    FROM #PayrollBase py
    INNER JOIN #ProfBase p ON p.PersonnelKey = py.PersonnelKey
    WHERE py.GenderValue <> p.GenderValue

    UNION ALL

    SELECT
        'Payroll_Sample -> dbo_PROF',
        4,
        'Race/Group Code Accuracy',
        py.PersonnelNumber,
        'Payroll_Sample GROUP_NAME first letter does not match dbo_PROF race/group code first letter.',
        py.GroupValue,
        p.GroupValue
    FROM #PayrollBase py
    INNER JOIN #ProfBase p ON p.PersonnelKey = py.PersonnelKey
    WHERE LEFT(py.GroupValue, 1) <> LEFT(p.GroupValue, 1)

    UNION ALL

    SELECT
        'Payroll_Sample -> dbo_PROF',
        5,
        'Birth Date Integrity',
        py.PersonnelNumber,
        'Payroll_Sample birth date does not match dbo_PROF birth date.',
        CONVERT(nvarchar(30), py.BirthDateValue, 23),
        CONVERT(nvarchar(30), p.BirthDateValue, 23)
    FROM #PayrollBase py
    INNER JOIN #ProfBase p ON p.PersonnelKey = py.PersonnelKey
    WHERE p.BirthDateValue IS NOT NULL
      AND (py.BirthDateValue IS NULL OR py.BirthDateValue <> p.BirthDateValue)
) Exceptions
ORDER BY DirectionLabel, ControlNumber, PersonnelNumber;

DROP TABLE #ProfBase;
DROP TABLE #PayrollBase;";

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
            var profByPersonnel = profRecords
                .Where(r => !string.IsNullOrWhiteSpace(r.PersonnelKey))
                .GroupBy(r => r.PersonnelKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var direction1 = BuildProfToPayrollDirection(profRecords, payrollByPersonnel, request);
            var direction2 = BuildPayrollToProfDirection(payrollRecords, profByPersonnel, request);

            var totalValidated = direction1.Controls.Sum(c => c.TotalTested) + direction2.Controls.Sum(c => c.TotalTested);
            var totalExceptions = direction1.TotalExceptions + direction2.TotalExceptions;
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
                PayrollPersonnelColumn = request.PayrollPersonnelColumn,
                PayrollEmploymentTypeColumn = request.PayrollEmploymentTypeColumn,
                PayrollGenderColumn = request.PayrollGenderColumn,
                PayrollGroupColumn = request.PayrollGroupColumn,
                PayrollBirthDateColumn = request.PayrollBirthDateColumn,
                ProfRecordCount = profRecords.Count,
                PayrollRecordCount = payrollRecords.Count,
                LinkedRecordCount = linkedRecordCount,
                ClientId = request.ClientId,
                Directions = [direction1, direction2],
                Exceptions = direction1.Exceptions.Concat(direction2.Exceptions).ToList()
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

            await using var cmd = conn.CreateCommand();
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

            await using var cmd = conn.CreateCommand();
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
            var direction = new Rule26DirectionResultViewModel
            {
                DirectionKey = "prof_to_payroll",
                DirectionLabel = "dbo_PROF -> Payroll_Sample",
                BaseTable = request.ProfTable,
                ReferenceTable = request.PayrollTable,
                BaseRecordCount = profRecords.Count
            };

            var controls = CreateControlShell();
            var linkedPairs = new List<(ProfRecord Prof, PayrollRecord Payroll)>();

            foreach (var prof in profRecords)
            {
                if (string.IsNullOrWhiteSpace(prof.PersonnelKey) || !payrollByPersonnel.TryGetValue(prof.PersonnelKey, out var payrollMatches) || payrollMatches.Count == 0)
                {
                    direction.Exceptions.Add(CreateException(
                        direction,
                        controls[0],
                        prof.PersonnelNumber,
                        null,
                        "Personnel number exists in dbo_PROF but not in Payroll_Sample.",
                        prof.PersonnelNumber,
                        ""));
                }
                else
                {
                    foreach (var payroll in payrollMatches)
                        linkedPairs.Add((prof, payroll));
                }
            }

            controls[0].TotalTested = profRecords.Count;
            controls[0].ExceptionCount = direction.Exceptions.Count(e => e.ControlNumber == 1);
            controls[0].Passed = controls[0].ExceptionCount == 0;

            direction.LinkedRecordCount = linkedPairs.Count;

            EvaluateLinkedControlsForProfDirection(direction, controls, linkedPairs);
            direction.Controls = controls;
            direction.TotalExceptions = direction.Exceptions.Count;
            return direction;
        }

        private Rule26DirectionResultViewModel BuildPayrollToProfDirection(
            List<PayrollRecord> payrollRecords,
            Dictionary<string, List<ProfRecord>> profByPersonnel,
            Rule26ValidationRequest request)
        {
            var direction = new Rule26DirectionResultViewModel
            {
                DirectionKey = "payroll_to_prof",
                DirectionLabel = "Payroll_Sample -> dbo_PROF",
                BaseTable = request.PayrollTable,
                ReferenceTable = request.ProfTable,
                BaseRecordCount = payrollRecords.Count
            };

            var controls = CreateControlShell();
            var linkedPairs = new List<(PayrollRecord Payroll, ProfRecord Prof)>();

            foreach (var payroll in payrollRecords)
            {
                if (string.IsNullOrWhiteSpace(payroll.PersonnelKey) || !profByPersonnel.TryGetValue(payroll.PersonnelKey, out var profMatches) || profMatches.Count == 0)
                {
                    direction.Exceptions.Add(CreateException(
                        direction,
                        controls[0],
                        payroll.PersonnelNumber,
                        payroll.PersonnelName,
                        "Personnel number exists in Payroll_Sample but not in dbo_PROF.",
                        payroll.PersonnelNumber,
                        ""));
                }
                else
                {
                    foreach (var prof in profMatches)
                        linkedPairs.Add((payroll, prof));
                }
            }

            controls[0].TotalTested = payrollRecords.Count;
            controls[0].ExceptionCount = direction.Exceptions.Count(e => e.ControlNumber == 1);
            controls[0].Passed = controls[0].ExceptionCount == 0;

            direction.LinkedRecordCount = linkedPairs.Count;

            EvaluateLinkedControlsForPayrollDirection(direction, controls, linkedPairs);
            direction.Controls = controls;
            direction.TotalExceptions = direction.Exceptions.Count;
            return direction;
        }

        private static List<Rule26ControlSummaryViewModel> CreateControlShell() =>
        [
            new()
            {
                ControlNumber = 1,
                ControlName = "Personnel Number Existence",
                Explanation = "Checks whether every base-table personnel number exists in the reference table."
            },
            new()
            {
                ControlNumber = 2,
                ControlName = "Employment Type Match",
                Explanation = "Compares Payroll PERMANENT_OR_TEMP to the first letter of dbo_PROF _041 for linked personnel."
            },
            new()
            {
                ControlNumber = 3,
                ControlName = "Gender Consistency",
                Explanation = "Compares Payroll GENDER to dbo_PROF _012 for linked personnel."
            },
            new()
            {
                ControlNumber = 4,
                ControlName = "Race/Group Code Accuracy",
                Explanation = "Compares the first letter of Payroll GROUP_NAME to the first letter of dbo_PROF _013 for linked personnel."
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
            List<(ProfRecord Prof, PayrollRecord Payroll)> linkedPairs)
        {
            foreach (var pair in linkedPairs)
            {
                if (AreSqlComparableValuesDifferent(GetFirstCharacterValue(pair.Prof.EmploymentType), NormalizeText(pair.Payroll.EmploymentType)))
                {
                    direction.Exceptions.Add(CreateException(
                        direction, controls[1], pair.Prof.PersonnelNumber, pair.Payroll.PersonnelName,
                        "dbo_PROF employment type first letter does not match Payroll_Sample PERMANENT_OR_TEMP.",
                        pair.Prof.EmploymentType, pair.Payroll.EmploymentType));
                }

                if (AreSqlComparableValuesDifferent(NormalizeText(pair.Prof.Gender), NormalizeText(pair.Payroll.Gender)))
                {
                    direction.Exceptions.Add(CreateException(
                        direction, controls[2], pair.Prof.PersonnelNumber, pair.Payroll.PersonnelName,
                        "dbo_PROF gender does not match Payroll_Sample gender.",
                        pair.Prof.Gender, pair.Payroll.Gender));
                }

                if (AreSqlComparableValuesDifferent(GetFirstCharacterValue(pair.Prof.GroupCode), GetFirstCharacterValue(pair.Payroll.GroupName)))
                {
                    direction.Exceptions.Add(CreateException(
                        direction, controls[3], pair.Prof.PersonnelNumber, pair.Payroll.PersonnelName,
                        "dbo_PROF race/group code first letter does not match Payroll_Sample GROUP_NAME first letter.",
                        pair.Prof.GroupCode, pair.Payroll.GroupName));
                }

                if (pair.Prof.BirthDate.HasValue &&
                    (!pair.Payroll.BirthDate.HasValue || pair.Prof.BirthDate.Value != pair.Payroll.BirthDate.Value))
                {
                    direction.Exceptions.Add(CreateException(
                        direction, controls[4], pair.Prof.PersonnelNumber, pair.Payroll.PersonnelName,
                        "dbo_PROF birth date does not match Payroll_Sample birth date.",
                        pair.Prof.BirthDate.Value.ToString("yyyy-MM-dd"), pair.Payroll.BirthDate?.ToString("yyyy-MM-dd") ?? ""));
                }
            }

            for (var i = 1; i < controls.Count; i++)
            {
                controls[i].TotalTested = direction.BaseRecordCount;
                controls[i].ExceptionCount = direction.Exceptions.Count(e => e.ControlNumber == controls[i].ControlNumber);
                controls[i].Passed = controls[i].ExceptionCount == 0;
            }
        }

        private void EvaluateLinkedControlsForPayrollDirection(
            Rule26DirectionResultViewModel direction,
            List<Rule26ControlSummaryViewModel> controls,
            List<(PayrollRecord Payroll, ProfRecord Prof)> linkedPairs)
        {
            foreach (var pair in linkedPairs)
            {
                if (AreSqlComparableValuesDifferent(NormalizeText(pair.Payroll.EmploymentType), GetFirstCharacterValue(pair.Prof.EmploymentType)))
                {
                    direction.Exceptions.Add(CreateException(
                        direction, controls[1], pair.Payroll.PersonnelNumber, pair.Payroll.PersonnelName,
                        "Payroll_Sample employment type does not match the first letter of dbo_PROF employment type.",
                        pair.Payroll.EmploymentType, pair.Prof.EmploymentType));
                }

                if (AreSqlComparableValuesDifferent(NormalizeText(pair.Payroll.Gender), NormalizeText(pair.Prof.Gender)))
                {
                    direction.Exceptions.Add(CreateException(
                        direction, controls[2], pair.Payroll.PersonnelNumber, pair.Payroll.PersonnelName,
                        "Payroll_Sample gender does not match dbo_PROF gender.",
                        pair.Payroll.Gender, pair.Prof.Gender));
                }

                if (AreSqlComparableValuesDifferent(GetFirstCharacterValue(pair.Payroll.GroupName), GetFirstCharacterValue(pair.Prof.GroupCode)))
                {
                    direction.Exceptions.Add(CreateException(
                        direction, controls[3], pair.Payroll.PersonnelNumber, pair.Payroll.PersonnelName,
                        "Payroll_Sample GROUP_NAME first letter does not match dbo_PROF race/group code first letter.",
                        pair.Payroll.GroupName, pair.Prof.GroupCode));
                }

                if (pair.Prof.BirthDate.HasValue &&
                    (!pair.Payroll.BirthDate.HasValue || pair.Payroll.BirthDate.Value != pair.Prof.BirthDate.Value))
                {
                    direction.Exceptions.Add(CreateException(
                        direction, controls[4], pair.Payroll.PersonnelNumber, pair.Payroll.PersonnelName,
                        "Payroll_Sample birth date does not match dbo_PROF birth date.",
                        pair.Payroll.BirthDate?.ToString("yyyy-MM-dd") ?? "", pair.Prof.BirthDate.Value.ToString("yyyy-MM-dd")));
                }
            }

            for (var i = 1; i < controls.Count; i++)
            {
                controls[i].TotalTested = direction.BaseRecordCount;
                controls[i].ExceptionCount = direction.Exceptions.Count(e => e.ControlNumber == controls[i].ControlNumber);
                controls[i].Passed = controls[i].ExceptionCount == 0;
            }
        }

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

            await using var command = connection.CreateCommand();
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
            command.Parameters.AddWithValue("@ExceptionsJSON", JsonConvert.SerializeObject(summary.Exceptions));
            command.Parameters.AddWithValue("@ResultsJSON", JsonConvert.SerializeObject(summary));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            var recordHash = ComputeHash($@"ValidationRun|Rule26|{runId}|{request.ClientId}|{request.Database}|{request.ProfTable}|{request.PayrollTable}|{summary.TotalValidated}|{summary.FailCount}|{summary.Status}|{DateTime.UtcNow:o}|{previousHash}");

            await using var updateCommand = connection.CreateCommand();
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

            await using var cmd = conn.CreateCommand();
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
            await using var countCommand = connection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            countCommand.Parameters.AddWithValue("@RunID", runId);
            var existingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            await using var deleteCommand = connection.CreateCommand();
            deleteCommand.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            deleteCommand.Parameters.AddWithValue("@RunID", runId);
            await deleteCommand.ExecuteNonQueryAsync();

            await using var updateCommand = connection.CreateCommand();
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
            await using var command = connection.CreateCommand();
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
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET Status = @Status WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@Status", status);
            await command.ExecuteNonQueryAsync();
        }

        private async Task SetRunCurrentStateAsync(SqlConnection connection, int runId, bool isCurrent)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET IsCurrent = @IsCurrent WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@IsCurrent", isCurrent);
            await command.ExecuteNonQueryAsync();
        }

        private async Task MarkPreviousRunsHistoricalAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateCommand();
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
            await using var command = connection.CreateCommand();
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

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            command.Parameters.AddWithValue("@Email", email);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private async Task<string?> GetEngagementRoleAsync(SqlConnection connection, int clientId, int userId)
        {
            await using var command = connection.CreateCommand();
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
            await using var command = connection.CreateCommand();
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
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private async Task<string?> GetLatestValidationHashAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateCommand();
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
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT TOP 1 ResultsJSON FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value
                ? null
                : DeserializeSummary(Convert.ToString(value));
        }

        private async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var command = connection.CreateCommand();
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
                ConnectTimeout = 30
            };

            var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static string BuildConnectionString(string server, string database, string driver) =>
            $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=30;";

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
                return JsonConvert.DeserializeObject<Rule26ValidationSummary>(json);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<int?> GetClientIdForRunAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateCommand();
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
    }
}
