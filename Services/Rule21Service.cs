using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.Helpers;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule21Service : IRule21Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;

        public Rule21Service(IConfiguration configuration)
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

                var autoTable = tables.FirstOrDefault(t =>
                        t.Equals("dbo_STUD", StringComparison.OrdinalIgnoreCase))
                    ?? tables.FirstOrDefault();

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

        public async Task<Rule21ColumnSelectionResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                var columns = await GetTableColumnsAsync(server, database, driver, tableName);
                var autoFilterColumn = columns.FirstOrDefault(c =>
                        c.Equals("_010", StringComparison.OrdinalIgnoreCase))
                    ?? columns.FirstOrDefault();

                return new Rule21ColumnSelectionResult
                {
                    Success = true,
                    Columns = columns,
                    AutoFilterColumn = autoFilterColumn,
                    AutoBreakdownColumn = null
                };
            }
            catch (Exception ex)
            {
                return new Rule21ColumnSelectionResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule21FilterValueResult> GetFilterValuesAsync(string server, string database, string driver, string tableName, string filterColumn)
        {
            try
            {
                ValidateObjectName(tableName);
                ValidateObjectName(filterColumn);

                var connStr = BuildConnectionString(server, database, driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var safeTable = Sanitise(tableName);
                var safeColumn = Sanitise(filterColumn);

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = $@"
SELECT TOP 20
    LTRIM(RTRIM(CAST([{safeColumn}] AS nvarchar(4000)))) AS FilterValue,
    COUNT(*) AS RecordCount
FROM [{safeTable}]
WHERE [{safeColumn}] IS NOT NULL
GROUP BY LTRIM(RTRIM(CAST([{safeColumn}] AS nvarchar(4000))))
ORDER BY COUNT(*) DESC, FilterValue ASC;";

                await using var reader = await cmd.ExecuteReaderAsync();
                var options = new List<Rule21FilterValueOption>();
                while (await reader.ReadAsync())
                {
                    var value = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    var count = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                    options.Add(new Rule21FilterValueOption
                    {
                        Value = value,
                        Count = count,
                        Label = $"{value} ({count:N0} records)"
                    });
                }

                return new Rule21FilterValueResult
                {
                    Success = true,
                    Options = options,
                    DefaultValue = options.FirstOrDefault()?.Value
                };
            }
            catch (Exception ex)
            {
                return new Rule21FilterValueResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule21VerifyResult> VerifyTableAsync(Rule21VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);

                var safeTable = Sanitise(request.TableName);
                var safeFilterColumn = Sanitise(request.FilterColumn);
                var parsedValues = ParseFilterValues(request.FilterValue);
                var normalizedValues = parsedValues
                    .Select(NormalizeComparableValue)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var totalRecords = await CountAsync(conn, $"SELECT COUNT(*) FROM [{safeTable}];");

                await using var countCommand = conn.CreateConfiguredCommand();
                var countPredicate = BuildFilterPredicate(countCommand, safeFilterColumn, parsedValues, normalizedValues);
                countCommand.CommandText = $@"
SELECT COUNT(*)
FROM [{safeTable}]
WHERE {countPredicate};";
                var matchingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

                await using var sampleCommand = conn.CreateConfiguredCommand();
                var samplePredicate = BuildFilterPredicate(sampleCommand, safeFilterColumn, parsedValues, normalizedValues);
                sampleCommand.CommandText = $@"
SELECT TOP 5 *
FROM [{safeTable}]
WHERE {samplePredicate}
ORDER BY NEWID();";

                await using var reader = await sampleCommand.ExecuteReaderAsync();
                var sampleRows = new List<Rule21SampleRowViewModel>();
                while (await reader.ReadAsync())
                {
                    var row = new Rule21SampleRowViewModel();
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        row.Values[reader.GetName(i)] = reader.IsDBNull(i)
                            ? null
                            : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                    }
                    sampleRows.Add(row);
                }

                return new Rule21VerifyResult
                {
                    Success = true,
                    TotalRecords = totalRecords,
                    MatchingCount = matchingCount,
                    SampleRows = sampleRows
                };
            }
            catch (Exception ex)
            {
                return new Rule21VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule21ValidationSummary> RunValidationAsync(Rule21ValidationRequest request, string? userEmail = null, string? userName = null)
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

                ApplyBrowserPreview(summary);

                return summary;
            }
            catch (Exception ex)
            {
                return new Rule21ValidationSummary
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

        public async Task<Rule21WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
    ISNULL(vr.StudTable, '') AS TableName,
    ISNULL(vr.DeceasedTable, '') AS FilterColumn,
    ISNULL(vr.StudColumn, '') AS BreakdownColumn,
    ISNULL(vr.DeceasedColumn, '') AS FilterValue,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 21
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

            var workspace = new Rule21WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                TableName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                FilterColumn = reader.IsDBNull(5) ? "" : reader.GetString(5),
                BreakdownColumn = reader.IsDBNull(6) ? "" : reader.GetString(6),
                FilterValue = reader.IsDBNull(7) ? "F" : reader.GetString(7),
                CurrentStatus = reader.IsDBNull(8) ? "" : reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9) ? null : reader.GetString(9),
                LastEditedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                SampleSize = deserializedSummary?.SampleSize ?? 1,
                ShowAllRecords = deserializedSummary?.ShowAllRecords ?? true,
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

        public async Task<Rule21RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON, vr.ExceptionsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 21;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            if (includeFullResults)
            {
                var fullRows = DeserializeRows(reader.IsDBNull(7) ? null : reader.GetString(7));
                if (fullRows.Count < summary.MatchingCount)
                {
                    var sourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5);
                    if (!string.IsNullOrWhiteSpace(sourceServer))
                    {
                        fullRows = await RegenerateFullRowsAsync(sourceServer, summary);
                    }
                }

                summary.MatchingRows = fullRows;
                summary.DisplayedCount = summary.MatchingRows.Count;
                summary.IsPreviewOnly = false;
                summary.PreviewLimit = 0;
            }
            else
            {
                ApplyBrowserPreview(summary);
            }

            var viewModel = new Rule21RunReviewViewModel
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

        public async Task<Rule21WorkspaceSaveResult> SaveWorkspaceAsync(Rule21ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                {
                    return new Rule21WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run the filter first so the workspace can be saved."
                    };
                }

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                {
                    return new Rule21WorkspaceSaveResult
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
    StudTable = @TableName,
    DeceasedTable = @FilterColumn,
    StudColumn = @BreakdownColumn,
    DeceasedColumn = @FilterValue,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID
  AND ClientID = @ClientID;";
                command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                command.Parameters.AddWithValue("@ClientID", request.ClientId);
                command.Parameters.AddWithValue("@HemisServer", request.Server);
                command.Parameters.AddWithValue("@AuditDatabase", request.Database);
                command.Parameters.AddWithValue("@TableName", request.TableName);
                command.Parameters.AddWithValue("@FilterColumn", request.FilterColumn);
                command.Parameters.AddWithValue("@BreakdownColumn", request.BreakdownColumn);
                command.Parameters.AddWithValue("@FilterValue", request.FilterValue);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule21|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|{request.TableName}|{request.FilterColumn}|{request.FilterValue}|{request.BreakdownColumn}|{request.SampleSize}|{request.ShowAllRecords}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule21WorkspaceSaveResult
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
                return new Rule21WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule21WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule21WorkspaceSaveResult
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
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID;";
                markEdit.Parameters.AddWithValue("@RunID", runId);
                markEdit.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule21|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule21WorkspaceSaveResult
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
                return new Rule21WorkspaceSaveResult
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

        public Task<string> GenerateSqlAsync(Rule21ValidationRequest request)
        {
            ValidateSqlRequest(request);

            var safeTable = Sanitise(request.TableName);
            var safeFilterColumn = Sanitise(request.FilterColumn);
            var parsedValues = ParseFilterValues(request.FilterValue);
            var normalizedValues = parsedValues
                .Select(NormalizeComparableValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var rawValueList = string.Join(", ", parsedValues.Select(value => $"'{EscapeSqlString(value)}'"));
            var normalizedValueList = string.Join(", ", normalizedValues.Select(value => $"'{EscapeSqlString(value)}'"));
            var trimmedExpression = $"LTRIM(RTRIM(CAST([{safeFilterColumn}] AS nvarchar(4000))))";
            var normalizedExpression = BuildNormalizedSqlExpression(trimmedExpression);
            var sqlFilterPredicate = $"({trimmedExpression} IN ({rawValueList}) OR {normalizedExpression} IN ({normalizedValueList}))";

            var sql = $@"-- ============================================================================
-- HEMIS RULE 21: FIRST TIME ENTERING STUDENTS FILTERED VALIDATION
-- ============================================================================
-- Table: [{safeTable}]
-- Filter Column: [{safeFilterColumn}]
-- Filter Values (raw): {string.Join(", ", parsedValues)}
-- Filter Values (normalized): {string.Join(", ", normalizedValues)}
-- Logic: 100% of the filtered dbo_STUD population is returned when [{safeFilterColumn}] matches an entered value
-- ============================================================================";

            sql += $@"

-- STEP 1: COUNT FILTERED RECORDS
SELECT COUNT(*) AS Matching_Record_Count
FROM [{safeTable}]
WHERE {sqlFilterPredicate};

-- STEP 2: BREAKDOWN OF SOURCE VALUES IN [{safeFilterColumn}]
SELECT
    LTRIM(RTRIM(CAST([{safeFilterColumn}] AS nvarchar(255)))) AS Filter_Column_Value,
    COUNT(*) AS Record_Count
FROM [{safeTable}]
GROUP BY LTRIM(RTRIM(CAST([{safeFilterColumn}] AS nvarchar(255))))
ORDER BY COUNT(*) DESC, Filter_Column_Value ASC;

-- STEP 3: RETURN ONLY FILTERED RECORDS
SELECT
    'Control_1' AS Control_Type,
    ROW_NUMBER() OVER (ORDER BY [{safeFilterColumn}] ASC) AS Sample_Number,
    ISNULL(CAST([{safeFilterColumn}] AS nvarchar(255)), '') AS FTE_Indicator_010,
    'PASS' AS Validation_Result,
    *
FROM [{safeTable}]
WHERE {sqlFilterPredicate}
ORDER BY [{safeFilterColumn}] ASC;

-- STEP 4: SUMMARY
SELECT
    (SELECT COUNT(*)
     FROM [{safeTable}]
     WHERE {sqlFilterPredicate}) AS Filtered_Record_Count,
    (SELECT COUNT(*)
     FROM [{safeTable}]
     WHERE {sqlFilterPredicate}) AS Pass_Record_Count,
    0 AS Fail_Record_Count;
";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule21ValidationSummary> AnalyseAsync(Rule21ValidationRequest request)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var safeTable = Sanitise(request.TableName);
            var safeFilterColumn = Sanitise(request.FilterColumn);
            var parsedValues = ParseFilterValues(request.FilterValue);
            var normalizedValues = parsedValues
                .Select(NormalizeComparableValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            await using var countCommand = conn.CreateConfiguredCommand();
            var countPredicate = BuildFilterPredicate(countCommand, safeFilterColumn, parsedValues, normalizedValues);
            countCommand.CommandText = $@"
SELECT COUNT(*)
FROM [{safeTable}]
WHERE {countPredicate};";
            var filteredCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
            var totalValidated = filteredCount;
            var passCount = filteredCount;
            var failCount = 0;

            var breakdown = new List<Rule21BreakdownItemViewModel>();
            await using (var breakdownCommand = conn.CreateConfiguredCommand())
            {
                breakdownCommand.CommandText = $@"
SELECT
    LTRIM(RTRIM(CAST([{safeFilterColumn}] AS nvarchar(255)))) AS BreakdownValue,
    COUNT(*) AS RecordCount
FROM [{safeTable}]
GROUP BY LTRIM(RTRIM(CAST([{safeFilterColumn}] AS nvarchar(255))))
ORDER BY COUNT(*) DESC, BreakdownValue ASC;";

                await using var breakdownReader = await breakdownCommand.ExecuteReaderAsync();
                while (await breakdownReader.ReadAsync())
                {
                    breakdown.Add(new Rule21BreakdownItemViewModel
                    {
                        Value = breakdownReader.IsDBNull(0) ? "(blank)" : breakdownReader.GetString(0),
                        Count = breakdownReader.IsDBNull(1) ? 0 : Convert.ToInt32(breakdownReader.GetValue(1))
                    });
                }
            }

            var matchingRows = filteredCount > 0
                ? await LoadFilteredRowsAsync(conn, safeTable, safeFilterColumn, request.FilterColumn, parsedValues, normalizedValues, BrowserPreviewRowLimit)
                : new List<Rule21ValidationRowRecord>();

            var displayedCount = matchingRows.Count;

            return new Rule21ValidationSummary
            {
                Success = true,
                TotalValidated = totalValidated,
                MatchingCount = passCount,
                DisplayedCount = displayedCount,
                PassCount = passCount,
                FailCount = failCount,
                ExceptionRate = totalValidated > 0 ? 100m : 0m,
                Status = totalValidated > 0 ? "COMPLETE" : "NO MATCHING DATA",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                TableName = request.TableName,
                FilterColumn = request.FilterColumn,
                FilterValue = string.Join(", ", parsedValues),
                BreakdownColumn = request.FilterColumn,
                SampleSize = Math.Max(request.SampleSize, 1),
                ShowAllRecords = true,
                Sampled = false,
                IsPreviewOnly = filteredCount > displayedCount,
                PreviewLimit = filteredCount > displayedCount ? displayedCount : 0,
                ClientId = request.ClientId,
                Breakdown = breakdown,
                MatchingRows = matchingRows
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule21ValidationRequest request, Rule21ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 21);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 21);
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
    @ClientID, @UserID, 21, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @TableName, @FilterColumn, @BreakdownColumn, @FilterValue,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "First Time Entering Students Filtered Validation");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@TableName", request.TableName);
            command.Parameters.AddWithValue("@FilterColumn", request.FilterColumn);
            command.Parameters.AddWithValue("@BreakdownColumn", request.BreakdownColumn);
            command.Parameters.AddWithValue("@FilterValue", request.FilterValue.Trim());
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary.MatchingRows)));
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
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule21|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
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

        private static void ValidateRequest(Rule21ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.TableName))
                throw new InvalidOperationException("Source table is required.");
            if (string.IsNullOrWhiteSpace(request.FilterColumn))
                throw new InvalidOperationException("Filter column is required.");
            if (string.IsNullOrWhiteSpace(request.FilterValue))
                throw new InvalidOperationException("Filter value is required.");

            ValidateObjectName(request.TableName);
            ValidateObjectName(request.FilterColumn);
            if (!string.IsNullOrWhiteSpace(request.BreakdownColumn))
                ValidateObjectName(request.BreakdownColumn);

            if (request.SampleSize <= 0)
                request.SampleSize = 1;

            request.ShowAllRecords = true;

            if (ParseFilterValues(request.FilterValue).Count == 0)
                throw new InvalidOperationException("Enter at least one filter value.");
        }

        private static void ValidateSqlRequest(Rule21ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.TableName))
                throw new InvalidOperationException("Source table is required.");
            if (string.IsNullOrWhiteSpace(request.FilterColumn))
                throw new InvalidOperationException("Filter column is required.");
            if (string.IsNullOrWhiteSpace(request.FilterValue))
                throw new InvalidOperationException("Filter value is required.");

            ValidateObjectName(request.TableName);
            ValidateObjectName(request.FilterColumn);
            if (!string.IsNullOrWhiteSpace(request.BreakdownColumn))
                ValidateObjectName(request.BreakdownColumn);

            if (ParseFilterValues(request.FilterValue).Count == 0)
                throw new InvalidOperationException("Enter at least one filter value.");
        }

        private static void ValidateRequest(Rule21VerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.TableName))
                throw new InvalidOperationException("Source table is required.");
            if (string.IsNullOrWhiteSpace(request.FilterColumn))
                throw new InvalidOperationException("Filter column is required.");
            if (string.IsNullOrWhiteSpace(request.FilterValue))
                throw new InvalidOperationException("Filter value is required.");

            ValidateObjectName(request.TableName);
            ValidateObjectName(request.FilterColumn);
            if (ParseFilterValues(request.FilterValue).Count == 0)
                throw new InvalidOperationException("Enter at least one filter value.");
        }

        private static List<string> ParseFilterValues(string? value) =>
            NumericFilterValueHelper.ParseValues(value);

        private static string NormalizeComparableValue(string? value) =>
            NumericFilterValueHelper.NormalizeNumericLikeValue(value);

        private static string BuildFilterPredicate(SqlCommand command, string safeFilterColumn, IReadOnlyList<string> rawValues, IReadOnlyList<string> normalizedValues)
        {
            var trimmedExpression = $"LTRIM(RTRIM(CAST([{safeFilterColumn}] AS nvarchar(4000))))";
            var normalizedExpression = BuildNormalizedSqlExpression(trimmedExpression);

            var rawParameterNames = new List<string>();
            for (var i = 0; i < rawValues.Count; i++)
            {
                var parameterName = $"@RawValue{i}";
                command.Parameters.AddWithValue(parameterName, rawValues[i]);
                rawParameterNames.Add(parameterName);
            }

            var normalizedParameterNames = new List<string>();
            for (var i = 0; i < normalizedValues.Count; i++)
            {
                var parameterName = $"@NormalizedValue{i}";
                command.Parameters.AddWithValue(parameterName, normalizedValues[i]);
                normalizedParameterNames.Add(parameterName);
            }

            return $"({trimmedExpression} IN ({string.Join(", ", rawParameterNames)}) OR {normalizedExpression} IN ({string.Join(", ", normalizedParameterNames)}))";
        }

        private static string BuildNormalizedSqlExpression(string trimmedExpression) =>
            NumericFilterValueHelper.BuildNormalizedSqlExpression(trimmedExpression);

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

        private async Task<Rule21ValidationSummary?> GetValidationSummaryAsync(SqlConnection connection, int runId)
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

        private static Rule21ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule21ValidationSummary>(decoded);
            }
            catch
            {
                return null;
            }
        }

        private static List<Rule21ValidationRowRecord> DeserializeRows(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<Rule21ValidationRowRecord>();

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<List<Rule21ValidationRowRecord>>(decoded) ?? new List<Rule21ValidationRowRecord>();
            }
            catch
            {
                return new List<Rule21ValidationRowRecord>();
            }
        }

        private static Rule21ValidationSummary CreateBrowserPreview(Rule21ValidationSummary summary)
        {
            var previewRows = summary.MatchingRows
                .Where(row => string.Equals(row.BreakdownValue, "PASS", StringComparison.OrdinalIgnoreCase))
                .Take(BrowserPreviewRowLimit)
                .ToList();

            if (previewRows.Count == 0)
                previewRows = summary.MatchingRows.Take(BrowserPreviewRowLimit).ToList();

            var sourceRowCount = Math.Max(summary.MatchingCount, summary.MatchingRows.Count);
            var isPreviewOnly = summary.IsPreviewOnly || sourceRowCount > previewRows.Count;
            var previewLimit = isPreviewOnly ? previewRows.Count : 0;

            return new Rule21ValidationSummary
            {
                Success = summary.Success,
                TotalValidated = summary.TotalValidated,
                MatchingCount = summary.MatchingCount,
                DisplayedCount = previewRows.Count,
                IsPreviewOnly = isPreviewOnly,
                PreviewLimit = previewLimit,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                TableName = summary.TableName,
                FilterColumn = summary.FilterColumn,
                FilterValue = summary.FilterValue,
                BreakdownColumn = summary.BreakdownColumn,
                SampleSize = summary.SampleSize,
                ShowAllRecords = summary.ShowAllRecords,
                Sampled = summary.Sampled,
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                Breakdown = summary.Breakdown
                    .Select(item => new Rule21BreakdownItemViewModel
                    {
                        Value = item.Value,
                        Count = item.Count
                    })
                    .ToList(),
                MatchingRows = previewRows,
                Warning = summary.Warning,
                Error = summary.Error
            };
        }

        private async Task<List<Rule21ValidationRowRecord>> RegenerateFullRowsAsync(string server, Rule21ValidationSummary summary)
        {
            var parsedValues = ParseFilterValues(summary.FilterValue);
            var normalizedValues = parsedValues
                .Select(NormalizeComparableValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var connStr = BuildConnectionString(server, summary.Database, "ODBC Driver 17 for SQL Server");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            return await LoadFilteredRowsAsync(
                conn,
                Sanitise(summary.TableName),
                Sanitise(summary.FilterColumn),
                summary.FilterColumn,
                parsedValues,
                normalizedValues,
                null);
        }

        private async Task<List<Rule21ValidationRowRecord>> LoadFilteredRowsAsync(
            SqlConnection conn,
            string safeTable,
            string safeFilterColumn,
            string requestedFilterColumn,
            List<string> parsedValues,
            List<string> normalizedValues,
            int? maxRows)
        {
            await using var dataCommand = conn.CreateConfiguredCommand();
            var dataPredicate = BuildFilterPredicate(dataCommand, safeFilterColumn, parsedValues, normalizedValues);
            var topClause = maxRows.HasValue && maxRows.Value > 0 ? $"TOP {maxRows.Value} " : "";
            dataCommand.CommandText = $@"
SELECT {topClause}
    'Control_1' AS Control_Type,
    ROW_NUMBER() OVER (ORDER BY [{safeFilterColumn}] ASC) AS Sample_Number,
    ISNULL(CAST([{safeFilterColumn}] AS nvarchar(255)), '') AS FTE_Indicator_010,
    'PASS' AS Validation_Result,
    *
FROM [{safeTable}]
WHERE {dataPredicate}
ORDER BY [{safeFilterColumn}] ASC;";

            await using var reader = await dataCommand.ExecuteReaderAsync();
            var rows = new List<Rule21ValidationRowRecord>();
            var validationNumber = 0;
            while (await reader.ReadAsync())
            {
                validationNumber++;
                var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    displayValues[reader.GetName(i)] = reader.IsDBNull(i)
                        ? null
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                }

                displayValues.TryGetValue(requestedFilterColumn, out var filterValue);
                displayValues.TryGetValue("Validation_Result", out var validationResult);

                rows.Add(new Rule21ValidationRowRecord
                {
                    ValidationNumber = validationNumber,
                    FilterValue = filterValue ?? "",
                    BreakdownValue = validationResult ?? "",
                    DisplayValues = displayValues
                });
            }

            return rows;
        }

        private static void ApplyBrowserPreview(Rule21ValidationSummary summary)
        {
            var preview = CreateBrowserPreview(summary);
            summary.DisplayedCount = preview.DisplayedCount;
            summary.IsPreviewOnly = preview.IsPreviewOnly;
            summary.PreviewLimit = preview.PreviewLimit;
            summary.MatchingRows = preview.MatchingRows;
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
