using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.Helpers;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule30Service : IRule30Service
    {
        private static readonly string[] DefaultExclusions = ["02202", "02301", "02302", "00708", "07201", "01501", "1501"];
        private readonly IConfiguration _configuration;

        public Rule30Service(IConfiguration configuration)
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

                var autoTable = tables.FirstOrDefault(t =>
                        t.Equals("dbo_PROF_VALIDATION_DETAIL", StringComparison.OrdinalIgnoreCase))
                    ?? tables.FirstOrDefault(t =>
                        t.Equals("PROF_VALIDATION_DETAIL", StringComparison.OrdinalIgnoreCase))
                    ?? tables.FirstOrDefault(t =>
                        t.Equals("dbo_CESM_VALIDATION_DETAIL", StringComparison.OrdinalIgnoreCase))
                    ?? tables.FirstOrDefault(t =>
                        t.EndsWith("PROF_VALIDATION_DETAIL", StringComparison.OrdinalIgnoreCase))
                    ?? tables.FirstOrDefault(t =>
                        t.Contains("PROF_VALIDATION_DETAIL", StringComparison.OrdinalIgnoreCase));

                return new TableListResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = autoTable
                };
            }
            catch (Exception ex)
            {
                return new TableListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule32ColumnSelectionResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                var columns = await GetTableColumnsAsync(server, database, driver, tableName);
                var autoErrorType = FindFirst(columns,
                    ["Error_Type", "ErrorType", "Erro_Type"],
                    ["error_type", "errortype", "fatal"]);
                var autoError = FindFirst(columns,
                    ["Error", "Erro", "Error_Code", "ErrorCode"],
                    ["error", "code"]);

                return new Rule32ColumnSelectionResult
                {
                    Success = true,
                    Columns = columns,
                    AutoErrorTypeColumn = autoErrorType,
                    AutoErrorColumn = autoError
                };
            }
            catch (Exception ex)
            {
                return new Rule32ColumnSelectionResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule32FilterValueResult> GetFilterValuesAsync(string server, string database, string driver, string tableName, string errorTypeColumn)
        {
            try
            {
                ValidateObjectName(tableName);
                ValidateObjectName(errorTypeColumn);

                var connStr = BuildConnectionString(server, database, driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var safeTable = Sanitise(tableName);
                var safeColumn = Sanitise(errorTypeColumn);

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
SELECT
    LTRIM(RTRIM(CAST([{safeColumn}] AS nvarchar(255)))) AS FilterValue,
    COUNT(*) AS RecordCount
FROM [{safeTable}]
WHERE [{safeColumn}] IS NOT NULL
  AND LTRIM(RTRIM(CAST([{safeColumn}] AS nvarchar(255)))) <> ''
GROUP BY LTRIM(RTRIM(CAST([{safeColumn}] AS nvarchar(255))))
ORDER BY COUNT(*) DESC, FilterValue ASC;";

                await using var reader = await cmd.ExecuteReaderAsync();
                var options = new List<Rule32FilterValueOption>();
                while (await reader.ReadAsync())
                {
                    var value = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    var count = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                    options.Add(new Rule32FilterValueOption
                    {
                        Value = value,
                        Count = count,
                        Label = $"{value.ToUpperInvariant()} ({count:N0} records)"
                    });
                }

                if (!options.Any(o => string.Equals(o.Value, "Fatal", StringComparison.OrdinalIgnoreCase)))
                {
                    options.Insert(0, new Rule32FilterValueOption
                    {
                        Value = "Fatal",
                        Count = 0,
                        Label = "FATAL (0 records)"
                    });
                }

                var defaultValue = options.FirstOrDefault(o =>
                    string.Equals(o.Value, "Fatal", StringComparison.OrdinalIgnoreCase))?.Value ?? options.FirstOrDefault()?.Value;

                return new Rule32FilterValueResult
                {
                    Success = true,
                    Options = options,
                    DefaultValue = defaultValue
                };
            }
            catch (Exception ex)
            {
                return new Rule32FilterValueResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule32VerifyResult> VerifyTableAsync(Rule32VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);

                var exclusions = ParseExclusions(request.ExclusionCodes);
                var normalizedExclusions = exclusions.Select(NormalizeErrorCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var safeTable = Sanitise(request.TableName);
                var safeErrorTypeColumn = Sanitise(request.ErrorTypeColumn);
                var safeErrorColumn = Sanitise(request.ErrorColumn);

                var totalRecords = await CountAsync(conn, $"SELECT COUNT(*) FROM [{safeTable}];");

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
SELECT
    CAST([{safeErrorColumn}] AS nvarchar(255)) AS ErrorCode
FROM [{safeTable}]
WHERE UPPER(LTRIM(RTRIM(ISNULL(CAST([{safeErrorTypeColumn}] AS nvarchar(255)), '')))) = UPPER(@FilterValue);";
                cmd.Parameters.AddWithValue("@FilterValue", request.ErrorTypeValue.Trim());

                await using var reader = await cmd.ExecuteReaderAsync();
                var totalFatal = 0;
                var excluded = 0;
                while (await reader.ReadAsync())
                {
                    totalFatal++;
                    var errorCode = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    if (IsExcluded(errorCode, normalizedExclusions))
                        excluded++;
                }

                return new Rule32VerifyResult
                {
                    Success = true,
                    TotalRecords = totalRecords,
                    TotalFatal = totalFatal,
                    ExcludedCount = excluded,
                    RemainingCount = totalFatal - excluded,
                    NormalizedExclusions = normalizedExclusions
                };
            }
            catch (Exception ex)
            {
                return new Rule32VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule32ValidationSummary> RunValidationAsync(Rule32ValidationRequest request, string? userEmail = null, string? userName = null)
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
                        summary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                return summary;
            }
            catch (Exception ex)
            {
                return new Rule32ValidationSummary
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

        public async Task<Rule32WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
    ISNULL(vr.StudTable, '') AS StudTable,
    ISNULL(vr.DeceasedTable, '') AS ErrorTypeColumn,
    ISNULL(vr.StudColumn, '') AS ErrorColumn,
    ISNULL(vr.DeceasedColumn, '') AS ErrorTypeValue,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 30
  AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = includeSummary
                ? DeserializeSummary(reader.IsDBNull(11) ? null : reader.GetString(11))
                : null;

            var workspace = new Rule32WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                TableName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                ErrorTypeColumn = reader.IsDBNull(5) ? "" : reader.GetString(5),
                ErrorColumn = reader.IsDBNull(6) ? "" : reader.GetString(6),
                ErrorTypeValue = reader.IsDBNull(7) ? "Fatal" : reader.GetString(7),
                CurrentStatus = reader.IsDBNull(8) ? "" : reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9) ? null : reader.GetString(9),
                LastEditedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Summary = summary,
                ExclusionCodes = string.Join(", ", summary != null ? summary.Exclusions : DefaultExclusions)
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

        public async Task<Rule32RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 30;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            var viewModel = new Rule32RunReviewViewModel
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

        public async Task<Rule32WorkspaceSaveResult> SaveWorkspaceAsync(Rule32ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                {
                    return new Rule32WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run validation before saving the workspace."
                    };
                }

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                {
                    return new Rule32WorkspaceSaveResult
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
                    existingSummary.ClientId = request.ClientId;
                    existingSummary.Database = request.Database;
                    existingSummary.TableName = request.TableName;
                    existingSummary.ErrorTypeColumn = request.ErrorTypeColumn;
                    existingSummary.ErrorColumn = request.ErrorColumn;
                    existingSummary.ErrorTypeValue = request.ErrorTypeValue;
                    existingSummary.Exclusions = ParseExclusions(request.ExclusionCodes);
                    existingSummary.NormalizedExclusions = existingSummary.Exclusions
                        .Select(NormalizeErrorCode)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    existingSummary.SavedRunId = request.RunId.Value;
                }

                await using var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer = @HemisServer,
    AuditDatabase = @AuditDatabase,
    StudTable = @StudTable,
    DeceasedTable = @ErrorTypeColumn,
    StudColumn = @ErrorColumn,
    DeceasedColumn = @ErrorTypeValue,
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
                command.Parameters.AddWithValue("@StudTable", request.TableName);
                command.Parameters.AddWithValue("@ErrorTypeColumn", request.ErrorTypeColumn);
                command.Parameters.AddWithValue("@ErrorColumn", request.ErrorColumn);
                command.Parameters.AddWithValue("@ErrorTypeValue", request.ErrorTypeValue);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@ResultsJSON", existingSummary == null
                    ? DBNull.Value
                    : JsonConvert.SerializeObject(existingSummary));
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule30|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|{request.TableName}|{request.ErrorTypeColumn}|{request.ErrorColumn}|{request.ErrorTypeValue}|{request.ExclusionCodes}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule32WorkspaceSaveResult
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
                return new Rule32WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule32WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule32WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Saved workspace was not found."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, clientId.Value);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash = await GetValidationRecordHashAsync(connection, runId);

                await using var markEdit = connection.CreateCommand();
                markEdit.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID;";
                markEdit.Parameters.AddWithValue("@RunID", runId);
                markEdit.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule30|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule32WorkspaceSaveResult
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
                return new Rule32WorkspaceSaveResult
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
        }

        public Task<string> GenerateSqlAsync(Rule32ValidationRequest request)
        {
            ValidateSqlRequest(request);

            var exclusions = ParseExclusions(request.ExclusionCodes);
            var normalizedExclusions = exclusions
                .Select(NormalizeErrorCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var safeTable = Sanitise(request.TableName);
            var safeErrorTypeColumn = Sanitise(request.ErrorTypeColumn);
            var safeErrorColumn = Sanitise(request.ErrorColumn);
            var exclusionList = string.Join(", ", exclusions.Select(e => $"'{EscapeSqlString(e)}'"));
            var normalizedList = string.Join(", ", normalizedExclusions.Select(e => $"'{EscapeSqlString(e)}'"));

            var sql = $@"-- ============================================================================
-- HEMIS RULE 30: FATAL ERRORS WITH EXCLUSIONS (PROF)
-- ============================================================================
-- Table: [{safeTable}]
-- Filter: [{safeErrorTypeColumn}] = '{EscapeSqlString(request.ErrorTypeValue)}'
-- Exclusions: {string.Join(", ", exclusions)}
-- Normalized exclusions: {string.Join(", ", normalizedExclusions)}
-- PASS if no fatal errors remain after exclusion filtering.
-- ============================================================================

IF OBJECT_ID('tempdb..#Classified') IS NOT NULL
    DROP TABLE #Classified;

SELECT
    ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Validation_Number,
    src.*,
    src.Error_Code_Raw,
    src.Error_Code_Normalized,
    CASE
        WHEN src.Error_Code_Raw IN ({exclusionList})
          OR src.Error_Code_Normalized IN ({normalizedList})
        THEN 'EXCLUDED'
        ELSE 'REMAINING'
    END AS Classification
INTO #Classified
FROM
(
    SELECT
        *,
        LTRIM(RTRIM(ISNULL(CAST([{safeErrorColumn}] AS nvarchar(255)), ''))) AS Error_Code_Raw,
        CASE
            WHEN LTRIM(RTRIM(ISNULL(CAST([{safeErrorColumn}] AS nvarchar(255)), ''))) = '' THEN ''
            ELSE
                CASE
                    WHEN PATINDEX('%[^0]%', LTRIM(RTRIM(ISNULL(CAST([{safeErrorColumn}] AS nvarchar(255)), '')))) = 0 THEN '0'
                    ELSE SUBSTRING(
                        LTRIM(RTRIM(ISNULL(CAST([{safeErrorColumn}] AS nvarchar(255)), ''))),
                        PATINDEX('%[^0]%', LTRIM(RTRIM(ISNULL(CAST([{safeErrorColumn}] AS nvarchar(255)), '')))),
                        LEN(LTRIM(RTRIM(ISNULL(CAST([{safeErrorColumn}] AS nvarchar(255)), ''))))
                    )
                END
        END AS Error_Code_Normalized
    FROM [{safeTable}]
    WHERE UPPER(LTRIM(RTRIM(ISNULL(CAST([{safeErrorTypeColumn}] AS nvarchar(255)), '')))) = UPPER('{EscapeSqlString(request.ErrorTypeValue)}')
) src;

SELECT * FROM #Classified;

SELECT
    COUNT(*) AS Total_Fatal,
    SUM(CASE WHEN Classification = 'EXCLUDED' THEN 1 ELSE 0 END) AS Excluded_Count,
    SUM(CASE WHEN Classification = 'REMAINING' THEN 1 ELSE 0 END) AS Remaining_Count,
    CASE
        WHEN SUM(CASE WHEN Classification = 'REMAINING' THEN 1 ELSE 0 END) = 0 THEN 'PASS'
        ELSE 'FAIL'
    END AS Validation_Result
FROM #Classified;

SELECT Error_Code_Raw AS Error_Code, COUNT(*) AS Excluded_Count
FROM #Classified
WHERE Classification = 'EXCLUDED'
GROUP BY Error_Code_Raw
ORDER BY COUNT(*) DESC, Error_Code_Raw ASC;

SELECT Error_Code_Raw AS Error_Code, COUNT(*) AS Remaining_Count
FROM #Classified
WHERE Classification = 'REMAINING'
GROUP BY Error_Code_Raw
ORDER BY COUNT(*) DESC, Error_Code_Raw ASC;

DROP TABLE #Classified;";

            return Task.FromResult(sql);
        }

        private async Task<Rule32ValidationSummary> AnalyseAsync(Rule32ValidationRequest request)
        {
            var exclusions = ParseExclusions(request.ExclusionCodes);
            var normalizedExclusions = exclusions
                .Select(NormalizeErrorCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var safeTable = Sanitise(request.TableName);
            var safeErrorTypeColumn = Sanitise(request.ErrorTypeColumn);
            var safeErrorColumn = Sanitise(request.ErrorColumn);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT *
FROM [{safeTable}]
WHERE UPPER(LTRIM(RTRIM(ISNULL(CAST([{safeErrorTypeColumn}] AS nvarchar(255)), '')))) = UPPER(@FilterValue);";
            cmd.Parameters.AddWithValue("@FilterValue", request.ErrorTypeValue.Trim());

            await using var reader = await cmd.ExecuteReaderAsync();
            var excludedRows = new List<Rule32ValidationRowRecord>();
            var remainingRows = new List<Rule32ValidationRowRecord>();
            var excludedBreakdown = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var remainingBreakdown = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var validationNumber = 0;

            while (await reader.ReadAsync())
            {
                validationNumber++;
                var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    var value = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                    displayValues[name] = value;
                }

                var errorTypeValue = displayValues.TryGetValue(request.ErrorTypeColumn, out var errorTypeRaw)
                    ? errorTypeRaw ?? ""
                    : "";
                var errorCode = displayValues.TryGetValue(request.ErrorColumn, out var errorRaw)
                    ? errorRaw ?? ""
                    : "";
                var normalizedErrorCode = NormalizeErrorCode(errorCode);
                var isExcluded = IsExcluded(errorCode, normalizedExclusions);
                var classification = isExcluded ? "EXCLUDED" : "REMAINING";

                var row = new Rule32ValidationRowRecord
                {
                    ValidationNumber = validationNumber,
                    ErrorTypeValue = errorTypeValue,
                    ErrorCode = errorCode,
                    NormalizedErrorCode = normalizedErrorCode,
                    Classification = classification,
                    ErrorMessage = FindFirstValue(displayValues, "Error_Message", "ErrorMessage", "Message"),
                    Description = FindFirstValue(displayValues, "Description", "Error_Description", "ErrorDescription"),
                    ElementInformation = FindFirstValue(displayValues, "Element_Information", "ElementInformation", "Element"),
                    DisplayValues = displayValues
                };

                if (isExcluded)
                {
                    excludedRows.Add(row);
                    IncrementCount(excludedBreakdown, string.IsNullOrWhiteSpace(errorCode) ? "(blank)" : errorCode);
                }
                else
                {
                    remainingRows.Add(row);
                    IncrementCount(remainingBreakdown, string.IsNullOrWhiteSpace(errorCode) ? "(blank)" : errorCode);
                }
            }

            var totalFatal = validationNumber;
            var excludedCount = excludedRows.Count;
            var remainingCount = remainingRows.Count;
            var rate = totalFatal > 0
                ? Math.Round((decimal)remainingCount / totalFatal * 100m, 2)
                : 0m;

            return new Rule32ValidationSummary
            {
                Success = true,
                TotalValidated = totalFatal,
                TotalFatal = totalFatal,
                ExcludedCount = excludedCount,
                RemainingCount = remainingCount,
                PassCount = excludedCount,
                FailCount = remainingCount,
                ExceptionRate = rate,
                Status = remainingCount == 0 ? "PASS" : "FAIL",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                TableName = request.TableName,
                ErrorTypeColumn = request.ErrorTypeColumn,
                ErrorColumn = request.ErrorColumn,
                ErrorTypeValue = request.ErrorTypeValue,
                ClientId = request.ClientId,
                Exclusions = exclusions,
                NormalizedExclusions = normalizedExclusions,
                ExcludedBreakdown = excludedBreakdown
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new Rule32BreakdownItemViewModel { ErrorCode = x.Key, Count = x.Value })
                    .ToList(),
                RemainingBreakdown = remainingBreakdown
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new Rule32BreakdownItemViewModel { ErrorCode = x.Key, Count = x.Value })
                    .ToList(),
                ExcludedRows = excludedRows,
                RemainingRows = remainingRows
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule32ValidationRequest request, Rule32ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 30);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 30);

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
    @ClientID, @UserID, 30, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @DeceasedTable, @StudColumn, @DeceasedColumn,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "Fatal Errors with Exclusions (PROF)");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.TableName);
            command.Parameters.AddWithValue("@DeceasedTable", request.ErrorTypeColumn);
            command.Parameters.AddWithValue("@StudColumn", request.ErrorColumn);
            command.Parameters.AddWithValue("@DeceasedColumn", request.ErrorTypeValue);
            command.Parameters.AddWithValue("@ExceptionsJSON", JsonConvert.SerializeObject(summary.RemainingRows));
            command.Parameters.AddWithValue("@ResultsJSON", JsonConvert.SerializeObject(summary));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCommand = connection.CreateCommand();
            hashCommand.CommandText = @"
UPDATE dbo.ValidationRuns
SET RecordHash = @RecordHash
WHERE RunID = @RunID;";
            hashCommand.Parameters.AddWithValue("@RunID", runId);
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule30|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
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

        private static void ValidateRequest(Rule32ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.TableName))
                throw new InvalidOperationException("Source table is required.");
            if (string.IsNullOrWhiteSpace(request.ErrorTypeColumn))
                throw new InvalidOperationException("Error type column is required.");
            if (string.IsNullOrWhiteSpace(request.ErrorColumn))
                throw new InvalidOperationException("Error column is required.");
            if (string.IsNullOrWhiteSpace(request.ErrorTypeValue))
                throw new InvalidOperationException("Filter value is required.");

            ValidateObjectName(request.TableName);
            ValidateObjectName(request.ErrorTypeColumn);
            ValidateObjectName(request.ErrorColumn);
        }

        private static void ValidateSqlRequest(Rule32ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.TableName))
                throw new InvalidOperationException("Source table is required.");
            if (string.IsNullOrWhiteSpace(request.ErrorTypeColumn))
                throw new InvalidOperationException("Error type column is required.");
            if (string.IsNullOrWhiteSpace(request.ErrorColumn))
                throw new InvalidOperationException("Error column is required.");
            if (string.IsNullOrWhiteSpace(request.ErrorTypeValue))
                throw new InvalidOperationException("Filter value is required.");

            ValidateObjectName(request.TableName);
            ValidateObjectName(request.ErrorTypeColumn);
            ValidateObjectName(request.ErrorColumn);
        }

        private static void ValidateRequest(Rule32VerifyRequest request)
        {
            ValidateRequest(new Rule32ValidationRequest
            {
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                TableName = request.TableName,
                ErrorTypeColumn = request.ErrorTypeColumn,
                ErrorColumn = request.ErrorColumn,
                ErrorTypeValue = request.ErrorTypeValue,
                ExclusionCodes = request.ExclusionCodes
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

        private static List<string> ParseExclusions(string? exclusionCodes) =>
            NumericFilterValueHelper.ParseValues(exclusionCodes, DefaultExclusions);

        private static string NormalizeErrorCode(string? code) =>
            NumericFilterValueHelper.NormalizeNumericLikeValue(code);

        private static bool IsExcluded(string? errorCode, IEnumerable<string> normalizedExclusions)
        {
            var raw = (errorCode ?? "").Trim();
            var normalized = NormalizeErrorCode(raw);

            foreach (var exclusion in normalizedExclusions)
            {
                if (string.Equals(raw, exclusion, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalized, exclusion, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? FindFirst(IEnumerable<string> columns, string[] exactMatches, string[] containsMatches)
        {
            foreach (var exact in exactMatches)
            {
                var match = columns.FirstOrDefault(c => c.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            foreach (var fragment in containsMatches)
            {
                var match = columns.FirstOrDefault(c => c.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            return columns.FirstOrDefault();
        }

        private static void IncrementCount(IDictionary<string, int> lookup, string key)
        {
            if (lookup.TryGetValue(key, out var count))
                lookup[key] = count + 1;
            else
                lookup[key] = 1;
        }

        private static string FindFirstValue(Dictionary<string, string?> values, params string[] candidateKeys)
        {
            foreach (var key in candidateKeys)
            {
                if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    return value!;
            }

            return "";
        }

        private static async Task<int?> GetClientIdForRunAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
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
            command.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = @SignoffRole;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@SignoffRole", signoffRole);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
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

        private async Task<Rule32ValidationSummary?> GetValidationSummaryAsync(SqlConnection connection, int runId)
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

        private static async Task<int> CountAsync(SqlConnection connection, string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        private static Rule32ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<Rule32ValidationSummary>(json);
            }
            catch
            {
                return null;
            }
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);
    }
}
