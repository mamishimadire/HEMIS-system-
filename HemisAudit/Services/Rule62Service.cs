using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using HemisAudit.Helpers;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule62Service : IRule62Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private const int SqlCommandTimeoutSeconds = SqlLargeDataExtensions.LargeDataCommandTimeoutSeconds;

        private readonly IConfiguration _configuration;

        public Rule62Service(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private static string BuildConnectionString(string server, string database, string driver)
        {
            if (server.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase))
            {
                var pipe = ResolveLocalDbPipe(server);
                if (pipe != null)
                    return $"Server={pipe};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=30;";
            }

            return $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";
        }

        private static string? ResolveLocalDbPipe(string server)
        {
            try
            {
                var instance = server.Contains('\\') ? server.Split('\\').Last().Trim() : "MSSQLLocalDB";
                using (var start = Process.Start(new ProcessStartInfo("sqllocaldb", $"start \"{instance}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                })!)
                {
                    start.WaitForExit(8000);
                }

                using var info = Process.Start(new ProcessStartInfo("sqllocaldb", $"info \"{instance}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                })!;

                var output = info.StandardOutput.ReadToEnd();
                info.WaitForExit(3000);

                var match = System.Text.RegularExpressions.Regex.Match(
                    output,
                    @"Instance pipe name:\s*(np:[^\r\n]+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            catch
            {
                return null;
            }
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

        public async Task<DatabaseListResult> GetDatabasesAsync(string server, string driver)
        {
            try
            {
                await using var connection = new SqlConnection(BuildConnectionString(server, "master", driver));
                await connection.OpenAsync();

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = "SELECT name FROM sys.databases WHERE name NOT IN ('master','tempdb','model','msdb') ORDER BY name;";

                await using var reader = await command.ExecuteReaderAsync();
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

        public async Task<Rule62TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
        {
            try
            {
                await using var connection = new SqlConnection(BuildConnectionString(server, database, driver));
                await connection.OpenAsync();

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME;";

                await using var reader = await command.ExecuteReaderAsync();
                var tables = new List<string>();
                while (await reader.ReadAsync())
                    tables.Add(reader.GetString(0));

                return new Rule62TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = FindFirst(tables, ["dbo_STUD", "STUD"], ["stud"]),
                    AutoQualTable = FindFirst(tables, ["dbo_QUAL", "QUAL"], ["qual"])
                };
            }
            catch (Exception ex)
            {
                return new Rule62TableDiscoveryResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule62ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                tableName = (tableName ?? "").Trim();
                ValidateObjectName(tableName);

                await using var connection = new SqlConnection(BuildConnectionString(server, database, driver));
                await connection.OpenAsync();

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName ORDER BY ORDINAL_POSITION;";
                command.Parameters.AddWithValue("@TableName", tableName);

                await using var reader = await command.ExecuteReaderAsync();
                var columns = new List<string>();
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                        columns.Add(reader.GetString(0));
                }

                return new Rule62ColumnDiscoveryResult { Success = true, Columns = columns };
            }
            catch (Exception ex)
            {
                return new Rule62ColumnDiscoveryResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule62VerifyResult> VerifyTablesAsync(Rule62VerifyRequest request)
        {
            try
            {
                NormalizeRequest(request.StudTable, request.QualTable, request.ColumnMapping, out var studTable, out var qualTable, out var mapping);
                var qualTypes = ParseQualTypes(request.QualTypeCodesText);
                var qualTypeSql = BuildTextInList(qualTypes.Select(code => code.ToUpperInvariant()));

                await using var connection = new SqlConnection(BuildConnectionString(request.Server, request.Database, request.Driver));
                await connection.OpenAsync();

                var studCount = await ExecuteCountAsync(connection, $"SELECT COUNT_BIG(*) FROM [{Sanitise(studTable)}];");

                await using var linkedCommand = connection.CreateConfiguredCommand();
                linkedCommand.CommandTimeout = SqlCommandTimeoutSeconds;
                linkedCommand.CommandText = $@"
SELECT COUNT_BIG(*)
FROM [{Sanitise(studTable)}] S
LEFT JOIN [{Sanitise(qualTable)}] Q
    ON LTRIM(RTRIM(CAST(S.[{mapping.StudQualCodeCol}] AS nvarchar(200))))
     = LTRIM(RTRIM(CAST(Q.[{mapping.QualQualCodeCol}] AS nvarchar(200))))
WHERE Q.[{mapping.QualQualCodeCol}] IS NOT NULL;";
                var qualLinkedCount = Convert.ToInt32(await linkedCommand.ExecuteScalarAsync());

                await using var configuredCommand = connection.CreateConfiguredCommand();
                configuredCommand.CommandTimeout = SqlCommandTimeoutSeconds;
                configuredCommand.CommandText = $@"
SELECT COUNT_BIG(*)
FROM [{Sanitise(studTable)}] S
LEFT JOIN [{Sanitise(qualTable)}] Q
    ON LTRIM(RTRIM(CAST(S.[{mapping.StudQualCodeCol}] AS nvarchar(200))))
     = LTRIM(RTRIM(CAST(Q.[{mapping.QualQualCodeCol}] AS nvarchar(200))))
WHERE UPPER(LTRIM(RTRIM(ISNULL(CAST(Q.[{mapping.QualTypeCol}] AS nvarchar(20)), '')))) IN ({qualTypeSql});";
                var configuredQualTypeCount = Convert.ToInt32(await configuredCommand.ExecuteScalarAsync());

                return new Rule62VerifyResult
                {
                    Success = true,
                    StudCount = studCount,
                    QualLinkedCount = qualLinkedCount,
                    ConfiguredQualTypeCount = configuredQualTypeCount
                };
            }
            catch (Exception ex)
            {
                return new Rule62VerifyResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule62ValidationSummary> RunValidationAsync(Rule62ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                NormalizeRequest(request.StudTable, request.QualTable, request.ColumnMapping, out var studTable, out var qualTable, out var mapping);
                request.StudTable = studTable;
                request.QualTable = qualTable;
                request.ColumnMapping = mapping;
                request.QualTypeCodesText = NormalizeQualTypeText(request.QualTypeCodesText);

                var summary = await AnalyseAsync(request, mapping);
                if (summary.Success && request.ClientId > 0)
                {
                    try
                    {
                        summary.SavedRunId = await SaveValidationRunAsync(request, summary, userEmail, userName);
                    }
                    catch (Exception ex)
                    {
                        summary.Success = false;
                        summary.Error = $"Analysis completed, but the run could not be saved: {ex.Message}";
                        return summary;
                    }
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule62ValidationSummary { Success = false, Error = ex.Message };
            }
        }

        private async Task<Rule62ValidationSummary> AnalyseAsync(Rule62ValidationRequest request, Rule62ColumnMapping mapping)
        {
            var qualTypes = ParseQualTypes(request.QualTypeCodesText);
            if (qualTypes.Count == 0)
                return new Rule62ValidationSummary { Success = false, Error = "No valid qualification type codes were specified." };

            var sql = BuildValidationSql(request.StudTable, request.QualTable, mapping, BuildTextInList(qualTypes.Select(code => code.ToUpperInvariant())));

            await using var connection = new SqlConnection(BuildConnectionString(request.Server, request.Database, request.Driver));
            await connection.OpenAsync();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync();
            var passRows = new List<Rule62ReviewRow>();
            var failRows = new List<Rule62ReviewRow>();
            var failedStudentKeys = new HashSet<int>();

            while (await reader.ReadAsync())
            {
                var row = new Rule62ReviewRow
                {
                    RowKey = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
                    StudentNo = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                    QualCode = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim(),
                    StudentId = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim(),
                    PostalPostcode = reader.IsDBNull(4) ? "" : reader.GetString(4).Trim(),
                    HomePostcode = reader.IsDBNull(5) ? "" : reader.GetString(5).Trim(),
                    EntranceCategory = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim(),
                    SecondaryCompletion = reader.IsDBNull(7) ? "" : reader.GetString(7).Trim(),
                    MatricAggregate = reader.IsDBNull(8) ? "" : reader.GetString(8).Trim(),
                    QualJoinCode = reader.IsDBNull(9) ? "" : reader.GetString(9).Trim(),
                    QualType = reader.IsDBNull(10) ? "" : reader.GetString(10).Trim(),
                    ErrorCode = reader.IsDBNull(11) ? "" : reader.GetString(11).Trim(),
                    ValidationResult = reader.IsDBNull(12) ? "" : reader.GetString(12).Trim().ToUpperInvariant(),
                    ValidationExplanation = reader.IsDBNull(13) ? "" : reader.GetString(13).Trim()
                };

                if (row.ValidationResult == "PASS")
                {
                    passRows.Add(row);
                }
                else
                {
                    failRows.Add(row);
                    failedStudentKeys.Add(row.RowKey);
                }
            }

            var totalCount = passRows.Count + failedStudentKeys.Count;
            var failCount = failedStudentKeys.Count;
            var passCount = passRows.Count;
            var exceptionDetailCount = failRows.Count;
            var exceptionRate = totalCount == 0
                ? 0m
                : Math.Round((decimal)failCount / totalCount * 100m, 2);

            for (var index = 0; index < failRows.Count; index++)
                failRows[index].RowNumber = index + 1;

            for (var index = 0; index < passRows.Count; index++)
                passRows[index].RowNumber = index + 1;

            return new Rule62ValidationSummary
            {
                Success = true,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                StudTable = request.StudTable,
                QualTable = request.QualTable,
                QualTypeCodesText = request.QualTypeCodesText,
                ColumnMapping = mapping,
                ClientId = request.ClientId,
                TotalCount = totalCount,
                PassCount = passCount,
                FailCount = failCount,
                ExceptionDetailCount = exceptionDetailCount,
                ExceptionRate = exceptionRate,
                Status = failCount == 0 ? "PASS" : "FAIL",
                PassRows = passRows,
                FailRows = failRows
            };
        }

        private static string BuildValidationSql(string studTable, string qualTable, Rule62ColumnMapping mapping, string qualTypeSql)
        {
            var stud = Sanitise(studTable);
            var qual = Sanitise(qualTable);

            return $@"
WITH Base AS (
    SELECT
        ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS RowKey,
        LTRIM(RTRIM(CAST(S.[{mapping.StudStudentNoCol}] AS nvarchar(100)))) AS StudentNo,
        LTRIM(RTRIM(CAST(S.[{mapping.StudQualCodeCol}] AS nvarchar(200)))) AS QualCode,
        LTRIM(RTRIM(CAST(S.[{mapping.StudIdCol}] AS nvarchar(100)))) AS StudentId,
        UPPER(LTRIM(RTRIM(CAST(S.[{mapping.StudPostalCodeCol}] AS nvarchar(50))))) AS PostalPostcode,
        UPPER(LTRIM(RTRIM(CAST(S.[{mapping.StudHomePostcodeCol}] AS nvarchar(50))))) AS HomePostcode,
        UPPER(LTRIM(RTRIM(CAST(S.[{mapping.StudEntranceCategoryCol}] AS nvarchar(50))))) AS EntranceCategory,
        UPPER(LTRIM(RTRIM(CAST(S.[{mapping.StudSecondaryCompletionCol}] AS nvarchar(50))))) AS SecondaryCompletion,
        LTRIM(RTRIM(CAST(S.[{mapping.StudMatricAggregateCol}] AS nvarchar(50)))) AS MatricAggregate,
        ISNULL(LTRIM(RTRIM(CAST(Q.[{mapping.QualQualCodeCol}] AS nvarchar(200)))), '') AS QualJoinCode,
        UPPER(ISNULL(LTRIM(RTRIM(CAST(Q.[{mapping.QualTypeCol}] AS nvarchar(20)))), '')) AS QualType,
        CASE
            WHEN UPPER(ISNULL(LTRIM(RTRIM(CAST(Q.[{mapping.QualTypeCol}] AS nvarchar(20)))), '')) IN ({qualTypeSql}) THEN 1
            ELSE 0
        END AS IsConfiguredQualType,
        CASE
            WHEN LTRIM(RTRIM(CAST(S.[{mapping.StudMatricAggregateCol}] AS nvarchar(50)))) <> ''
             AND LTRIM(RTRIM(CAST(S.[{mapping.StudMatricAggregateCol}] AS nvarchar(50)))) NOT LIKE '%[^0-9]%'
                THEN TRY_CONVERT(int, LTRIM(RTRIM(CAST(S.[{mapping.StudMatricAggregateCol}] AS nvarchar(50)))))
            ELSE NULL
        END AS MatricAggregateInt,
        CASE
            WHEN LTRIM(RTRIM(CAST(S.[{mapping.StudMatricAggregateCol}] AS nvarchar(50)))) <> ''
             AND LTRIM(RTRIM(CAST(S.[{mapping.StudMatricAggregateCol}] AS nvarchar(50)))) NOT LIKE '%[^0-9]%'
                THEN TRY_CONVERT(int, LTRIM(RTRIM(CAST(S.[{mapping.StudMatricAggregateCol}] AS nvarchar(50)))))
            ELSE 0
        END AS ValLikeAggregate
    FROM [{stud}] S
    LEFT JOIN [{qual}] Q
        ON LTRIM(RTRIM(CAST(S.[{mapping.StudQualCodeCol}] AS nvarchar(200))))
         = LTRIM(RTRIM(CAST(Q.[{mapping.QualQualCodeCol}] AS nvarchar(200))))
),
Flagged AS (
    SELECT *,
        CASE WHEN NOT (PostalPostcode IN ('XXXX', 'YYYY', 'ZZZZ') OR PostalPostcode LIKE '[0-9][0-9][0-9][0-9]') THEN 1 ELSE 0 END AS HasErr07201,
        CASE WHEN NOT (HomePostcode IN ('XXXX', 'YYYY', 'ZZZZ') OR HomePostcode LIKE '[0-9][0-9][0-9][0-9]') THEN 1 ELSE 0 END AS HasErr01501,
        CASE WHEN SecondaryCompletion = 'ZZ' AND EntranceCategory = 'F' AND IsConfiguredQualType = 1 THEN 1 ELSE 0 END AS HasErr02202,
        CASE WHEN MatricAggregate = '' OR MatricAggregate LIKE '%[^0-9]%' OR MatricAggregateInt IS NULL OR MatricAggregateInt > 2600 THEN 1 ELSE 0 END AS HasErr02301,
        CASE WHEN ValLikeAggregate <= 0 AND EntranceCategory = 'F' AND IsConfiguredQualType = 1 THEN 1 ELSE 0 END AS HasErr02302
    FROM Base
),
Results AS (
    SELECT
        RowKey, StudentNo, QualCode, StudentId, PostalPostcode, HomePostcode, EntranceCategory, SecondaryCompletion,
        MatricAggregate, QualJoinCode, QualType,
        '' AS ErrorCode,
        'PASS' AS ValidationResult,
        'PASS: No Rule 62 SQLVALPAC exception code matched this STUD row.' AS ValidationExplanation
    FROM Flagged
    WHERE HasErr07201 = 0 AND HasErr01501 = 0 AND HasErr02202 = 0 AND HasErr02301 = 0 AND HasErr02302 = 0

    UNION ALL

    SELECT
        RowKey, StudentNo, QualCode, StudentId, PostalPostcode, HomePostcode, EntranceCategory, SecondaryCompletion,
        MatricAggregate, QualJoinCode, QualType,
        '07201' AS ErrorCode,
        'FAIL' AS ValidationResult,
        CASE
            WHEN PostalPostcode = ''
                THEN '07201: STUD.{mapping.StudPostalCodeCol} is blank and does not resolve to a valid postcode or approved placeholder (XXXX, YYYY, ZZZZ).'
            ELSE '07201: STUD.{mapping.StudPostalCodeCol} (' + PostalPostcode + ') is not a four-digit postcode or approved placeholder (XXXX, YYYY, ZZZZ).'
        END AS ValidationExplanation
    FROM Flagged
    WHERE HasErr07201 = 1

    UNION ALL

    SELECT
        RowKey, StudentNo, QualCode, StudentId, PostalPostcode, HomePostcode, EntranceCategory, SecondaryCompletion,
        MatricAggregate, QualJoinCode, QualType,
        '01501' AS ErrorCode,
        'FAIL' AS ValidationResult,
        CASE
            WHEN HomePostcode = ''
                THEN '01501: STUD.{mapping.StudHomePostcodeCol} is blank and does not resolve to a valid postcode or approved placeholder (XXXX, YYYY, ZZZZ).'
            ELSE '01501: STUD.{mapping.StudHomePostcodeCol} (' + HomePostcode + ') is not a four-digit postcode or approved placeholder (XXXX, YYYY, ZZZZ).'
        END AS ValidationExplanation
    FROM Flagged
    WHERE HasErr01501 = 1

    UNION ALL

    SELECT
        RowKey, StudentNo, QualCode, StudentId, PostalPostcode, HomePostcode, EntranceCategory, SecondaryCompletion,
        MatricAggregate, QualJoinCode, QualType,
        '02202' AS ErrorCode,
        'FAIL' AS ValidationResult,
        '02202: STUD.{mapping.StudSecondaryCompletionCol} = ZZ while STUD.{mapping.StudEntranceCategoryCol} = F and QUAL.{mapping.QualTypeCol} (' + ISNULL(NULLIF(QualType, ''), 'blank') + ') is in the configured dashboard list.' AS ValidationExplanation
    FROM Flagged
    WHERE HasErr02202 = 1

    UNION ALL

    SELECT
        RowKey, StudentNo, QualCode, StudentId, PostalPostcode, HomePostcode, EntranceCategory, SecondaryCompletion,
        MatricAggregate, QualJoinCode, QualType,
        '02301' AS ErrorCode,
        'FAIL' AS ValidationResult,
        CASE
            WHEN MatricAggregate = ''
                THEN '02301: STUD.{mapping.StudMatricAggregateCol} is blank and therefore outside the allowed 0 to 2600 integer range.'
            WHEN MatricAggregate LIKE '%[^0-9]%'
                THEN '02301: STUD.{mapping.StudMatricAggregateCol} (' + MatricAggregate + ') contains non-digit characters or signs.'
            WHEN MatricAggregateInt > 2600
                THEN '02301: STUD.{mapping.StudMatricAggregateCol} (' + MatricAggregate + ') is greater than 2600.'
            ELSE '02301: STUD.{mapping.StudMatricAggregateCol} (' + MatricAggregate + ') is outside the allowed 0 to 2600 integer range.'
        END AS ValidationExplanation
    FROM Flagged
    WHERE HasErr02301 = 1

    UNION ALL

    SELECT
        RowKey, StudentNo, QualCode, StudentId, PostalPostcode, HomePostcode, EntranceCategory, SecondaryCompletion,
        MatricAggregate, QualJoinCode, QualType,
        '02302' AS ErrorCode,
        'FAIL' AS ValidationResult,
        '02302: STUD.{mapping.StudMatricAggregateCol} (' + ISNULL(NULLIF(MatricAggregate, ''), 'blank') + ') resolves to zero or less while STUD.{mapping.StudEntranceCategoryCol} = F and QUAL.{mapping.QualTypeCol} (' + ISNULL(NULLIF(QualType, ''), 'blank') + ') is in the configured dashboard list.' AS ValidationExplanation
    FROM Flagged
    WHERE HasErr02302 = 1
)
SELECT
    RowKey, StudentNo, QualCode, StudentId, PostalPostcode, HomePostcode, EntranceCategory, SecondaryCompletion,
    MatricAggregate, QualJoinCode, QualType, ErrorCode, ValidationResult, ValidationExplanation
FROM Results
ORDER BY
    CASE WHEN ValidationResult = 'FAIL' THEN 0 ELSE 1 END,
    RowKey,
    ErrorCode;";
        }

        public string GenerateSql(Rule62ValidationRequest request)
        {
            NormalizeRequest(request.StudTable, request.QualTable, request.ColumnMapping, out var studTable, out var qualTable, out var mapping);
            var qualTypes = ParseQualTypes(request.QualTypeCodesText);
            var qualTypeSql = BuildTextInList(qualTypes.Select(code => code.ToUpperInvariant()));

            return $@"-- ============================================================
-- HEMIS RULE 62 - SQLVALPAC Approved Exception Review
-- Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}
-- Database  : {request.Database}
-- Tables    : [{Sanitise(studTable)}] STUD | [{Sanitise(qualTable)}] QUAL
-- Join      : STUD.{mapping.StudQualCodeCol} -> QUAL.{mapping.QualQualCodeCol}
-- Dashboard : QUAL.{mapping.QualTypeCol} IN ({string.Join(", ", qualTypes)})
-- Error rows: 07201, 01501, 02202, 02301, 02302
-- Reference : Exceptions are surfaced with their SQLVALPAC error code.
-- ============================================================
{BuildValidationSql(studTable, qualTable, mapping, qualTypeSql)}";
        }

        private async Task<int> SaveValidationRunAsync(Rule62ValidationRequest request, Rule62ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 62);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 62);
            var persisted = CloneSummaryForPersistence(summary);

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
    @ClientID, @UserID, 62, 'SQLVALPAC Approved Exception Review', @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @QualTable, @QualTypeCodesText, @StudEntranceCategoryCol,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";

            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalCount);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.StudTable);
            command.Parameters.AddWithValue("@QualTable", request.QualTable);
            command.Parameters.AddWithValue("@QualTypeCodesText", request.QualTypeCodesText);
            command.Parameters.AddWithValue("@StudEntranceCategoryCol", request.ColumnMapping.StudEntranceCategoryCol);
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persisted.FailRows)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persisted)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCommand = connection.CreateConfiguredCommand();
            hashCommand.CommandText = "UPDATE dbo.ValidationRuns SET RecordHash = @RecordHash WHERE RunID = @RunID;";
            hashCommand.Parameters.AddWithValue("@RunID", runId);
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($"ValidationRun|Rule62|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID AND RuleNumber = 62;";
            command.Parameters.AddWithValue("@RunID", runId);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        public async Task<Rule62WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID, vr.ClientID,
    ISNULL(vr.HemisServer, '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable, '') AS StudTable,
    ISNULL(vr.DeceasedTable, '') AS QualTable,
    ISNULL(vr.StudColumn, '') AS QualTypeCodesText,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName, vr.LastEditedAt, vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = 62 AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(10) ? null : reader.GetString(10));
            if (summary != null)
                ApplyBrowserPreview(summary);

            var workspace = new Rule62WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                StudTable = reader.IsDBNull(4) ? "dbo_STUD" : reader.GetString(4),
                QualTable = reader.IsDBNull(5) ? "dbo_QUAL" : reader.GetString(5),
                QualTypeCodesText = reader.IsDBNull(6) ? "01, 11, 02, 03, 21, 22, 23" : reader.GetString(6),
                CurrentStatus = reader.IsDBNull(7) ? "" : reader.GetString(7),
                LastEditedByUserName = reader.IsDBNull(8) ? null : reader.GetString(8),
                LastEditedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                Summary = summary,
                ColumnMapping = summary?.ColumnMapping ?? new Rule62ColumnMapping()
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
            var mySignoff = signoffs.FirstOrDefault(s =>
                ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff = mySignoff != null;
            workspace.CurrentUserSignoffComment = mySignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved = await IsWorkspaceSavedAsync(connection, workspace.RunId!.Value);

            if (workspace.Summary != null)
                workspace.Summary.SavedRunId = workspace.RunId;

            return workspace;
        }

        public async Task<Rule62RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID AND vr.RuleNumber = 62;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            ApplyBrowserPreview(summary);

            var clientId = reader.GetInt32(1);
            var review = new Rule62RunReviewViewModel
            {
                RunId = reader.GetInt32(0),
                ClientId = clientId,
                IsCurrentRun = !reader.IsDBNull(2) && reader.GetBoolean(2),
                EngagementName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                MaconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Summary = summary
            };

            await reader.CloseAsync();

            summary.SavedRunId = review.RunId;
            review.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";

            var signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            review.Signoffs = signoffs;
            review.HasDataAnalystSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            review.GeneratedSql = GenerateSql(new Rule62ValidationRequest
            {
                ClientId = clientId,
                Database = summary.Database,
                StudTable = summary.StudTable,
                QualTable = summary.QualTable,
                QualTypeCodesText = summary.QualTypeCodesText,
                ColumnMapping = summary.ColumnMapping
            });

            return review;
        }

        public async Task<Rule62WorkspaceSaveResult> SaveWorkspaceAsync(Rule62ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                await EnsureClientNotArchivedAsync(connection, request.ClientId);
                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                    return new Rule62WorkspaceSaveResult { Success = false, Error = "Your account could not be resolved in the system database." };

                if (!request.RunId.HasValue || request.RunId.Value <= 0)
                    return new Rule62WorkspaceSaveResult { Success = false, Error = "No saved run exists. Run Rule 62 first." };

                NormalizeRequest(request.StudTable, request.QualTable, request.ColumnMapping, out var studTable, out var qualTable, out var mapping);
                request.StudTable = studTable;
                request.QualTable = qualTable;
                request.ColumnMapping = mapping;
                request.QualTypeCodesText = NormalizeQualTypeText(request.QualTypeCodesText);

                var cleared = await ClearSignoffsAsync(connection, request.RunId.Value);
                var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 62);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns SET
    HemisServer          = @HemisServer,
    AuditDatabase        = @AuditDatabase,
    StudTable            = @StudTable,
    DeceasedTable        = @QualTable,
    StudColumn           = @QualTypeCodesText,
    DeceasedColumn       = @StudEntranceCategoryCol,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    WorkspaceSavedAt     = GETDATE(),
    Status               = 'Needs Review',
    RecordHash           = @RecordHash
WHERE RunID = @RunID AND RuleNumber = 62;";
                command.Parameters.AddWithValue("@HemisServer", request.Server);
                command.Parameters.AddWithValue("@AuditDatabase", request.Database);
                command.Parameters.AddWithValue("@StudTable", request.StudTable);
                command.Parameters.AddWithValue("@QualTable", request.QualTable);
                command.Parameters.AddWithValue("@QualTypeCodesText", request.QualTypeCodesText);
                command.Parameters.AddWithValue("@StudEntranceCategoryCol", request.ColumnMapping.StudEntranceCategoryCol);
                command.Parameters.AddWithValue("@LastEditedByUserName", reviewerName ?? reviewerEmail);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($"WorkspaceSave|Rule62|{request.RunId.Value}|{request.ClientId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                await command.ExecuteNonQueryAsync();

                var storedSummary = await GetStoredSummaryAsync(request.RunId.Value);
                if (storedSummary != null)
                {
                    storedSummary.StudTable = request.StudTable;
                    storedSummary.QualTable = request.QualTable;
                    storedSummary.QualTypeCodesText = request.QualTypeCodesText;
                    storedSummary.ColumnMapping = request.ColumnMapping;
                    await UpdateStoredSummaryAsync(connection, request.RunId.Value, storedSummary);
                }

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail);
                return new Rule62WorkspaceSaveResult
                {
                    Success = true,
                    Message = cleared > 0 ? $"Workspace saved. {cleared} signoff(s) were cleared." : "Workspace saved.",
                    SignoffsCleared = cleared > 0,
                    ClearedSignoffCount = cleared,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule62WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule62WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                    return new Rule62WorkspaceSaveResult { Success = false, Error = "Saved run not found." };

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                var cleared = await ClearSignoffsAsync(connection, runId);
                var previousHash = await GetLatestValidationHashAsync(connection, clientId.Value, 62);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns SET
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    RecordHash           = @RecordHash
WHERE RunID = @RunID;";
                command.Parameters.AddWithValue("@LastEditedByUserName", reviewerName ?? reviewerEmail);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($"BeginWorkspaceEdit|Rule62|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                command.Parameters.AddWithValue("@RunID", runId);
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail);
                return new Rule62WorkspaceSaveResult
                {
                    Success = true,
                    Message = cleared > 0 ? $"Workspace unlocked for editing. {cleared} signoff(s) cleared." : "Workspace unlocked for editing.",
                    SignoffsCleared = cleared > 0,
                    ClearedSignoffCount = cleared,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule62WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail)
                ?? throw new InvalidOperationException("Your account could not be resolved in the system database.");
            var role = await GetRunEngagementRoleAsync(connection, runId, reviewerId)
                ?? throw new InvalidOperationException("You are not assigned to this engagement.");
            var clientId = await GetClientIdForRunAsync(connection, runId)
                ?? throw new InvalidOperationException("Validation run not found.");

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND ReviewerID = @ReviewerID)
    UPDATE dbo.ReviewSignoffs SET SignoffRole = @Role, ReviewType = 'Final', Comment = @Comment, SignedOffAt = GETDATE() WHERE RunID = @RunID AND ReviewerID = @ReviewerID;
ELSE
    INSERT INTO dbo.ReviewSignoffs (ClientID, RunID, ReviewerID, SignoffRole, ReviewType, Comment, SignedOffAt)
    VALUES (@ClientID, @RunID, @ReviewerID, @Role, 'Final', @Comment, GETDATE());";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ReviewerID", reviewerId);
            command.Parameters.AddWithValue("@Comment", string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim());
            command.Parameters.AddWithValue("@Role", role);
            await command.ExecuteNonQueryAsync();
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail)
                ?? throw new InvalidOperationException("Your account could not be resolved.");

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND ReviewerID = @ReviewerID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ReviewerID", reviewerId);
            await command.ExecuteNonQueryAsync();
        }

        private static void ApplyBrowserPreview(Rule62ValidationSummary? summary)
        {
            if (summary == null)
                return;

            var failRows = summary.FailRows ?? new List<Rule62ReviewRow>();
            var passRows = summary.PassRows ?? new List<Rule62ReviewRow>();

            summary.IsPreviewOnly = failRows.Count > BrowserPreviewRowLimit || passRows.Count > BrowserPreviewRowLimit;
            summary.FailRows = failRows.Take(BrowserPreviewRowLimit).ToList();
            summary.PassRows = passRows.Take(BrowserPreviewRowLimit).ToList();
            summary.PreviewLimit = summary.IsPreviewOnly ? BrowserPreviewRowLimit : 0;
        }

        public async Task<Rule62ValidationSummary?> GetStoredSummaryAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 ResultsJSON
FROM dbo.ValidationRuns
WHERE RunID = @RunID AND RuleNumber = 62;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(0) ? null : reader.GetString(0));
            if (summary != null)
                summary.SavedRunId = runId;

            return summary;
        }

        private static Rule62ValidationSummary CloneSummaryForPersistence(Rule62ValidationSummary source) =>
            JsonConvert.DeserializeObject<Rule62ValidationSummary>(JsonConvert.SerializeObject(source))
            ?? new Rule62ValidationSummary();

        private static Rule62ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule62ValidationSummary>(decoded);
            }
            catch
            {
                return null;
            }
        }

        private static async Task UpdateStoredSummaryAsync(SqlConnection connection, int runId, Rule62ValidationSummary summary)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.ValidationRuns
SET ResultsJSON = @ResultsJSON
WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            await command.ExecuteNonQueryAsync();
        }

        private static void NormalizeRequest(
            string studTableIn,
            string qualTableIn,
            Rule62ColumnMapping? mappingIn,
            out string studTable,
            out string qualTable,
            out Rule62ColumnMapping mapping)
        {
            studTable = (studTableIn ?? "dbo_STUD").Trim();
            qualTable = (qualTableIn ?? "dbo_QUAL").Trim();
            mapping = mappingIn ?? new Rule62ColumnMapping();

            mapping.StudStudentNoCol = ColumnOrDefault(mapping.StudStudentNoCol, "_007");
            mapping.StudQualCodeCol = ColumnOrDefault(mapping.StudQualCodeCol, "_001");
            mapping.StudIdCol = ColumnOrDefault(mapping.StudIdCol, "_008");
            mapping.StudPostalCodeCol = ColumnOrDefault(mapping.StudPostalCodeCol, "_072");
            mapping.StudHomePostcodeCol = ColumnOrDefault(mapping.StudHomePostcodeCol, "_015");
            mapping.StudEntranceCategoryCol = ColumnOrDefault(mapping.StudEntranceCategoryCol, "_010");
            mapping.StudSecondaryCompletionCol = ColumnOrDefault(mapping.StudSecondaryCompletionCol, "_022");
            mapping.StudMatricAggregateCol = ColumnOrDefault(mapping.StudMatricAggregateCol, "_023");
            mapping.QualQualCodeCol = ColumnOrDefault(mapping.QualQualCodeCol, "_001");
            mapping.QualTypeCol = ColumnOrDefault(mapping.QualTypeCol, "_005");

            ValidateObjectName(studTable);
            ValidateObjectName(qualTable);
            ValidateObjectName(mapping.StudStudentNoCol);
            ValidateObjectName(mapping.StudQualCodeCol);
            ValidateObjectName(mapping.StudIdCol);
            ValidateObjectName(mapping.StudPostalCodeCol);
            ValidateObjectName(mapping.StudHomePostcodeCol);
            ValidateObjectName(mapping.StudEntranceCategoryCol);
            ValidateObjectName(mapping.StudSecondaryCompletionCol);
            ValidateObjectName(mapping.StudMatricAggregateCol);
            ValidateObjectName(mapping.QualQualCodeCol);
            ValidateObjectName(mapping.QualTypeCol);
        }

        private static string ColumnOrDefault(string? value, string defaultValue) =>
            string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();

        private static string NormalizeQualTypeText(string? value)
        {
            var codes = ParseQualTypes(value);
            return codes.Count == 0 ? "01, 11, 02, 03, 21, 22, 23" : string.Join(", ", codes);
        }

        private static List<string> ParseQualTypes(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            return text.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildTextInList(IEnumerable<string> values) =>
            string.Join(", ", values.Select(value => SqlStringLiteral(value.ToUpperInvariant())));

        private static string SqlStringLiteral(string value) =>
            $"'{(value ?? string.Empty).Replace("'", "''")}'";

        private static string? FindFirst(List<string> items, string[] exactMatches, string[] partials)
        {
            foreach (var exact in exactMatches)
            {
                if (items.Any(item => string.Equals(item, exact, StringComparison.OrdinalIgnoreCase)))
                    return exact;
            }

            foreach (var partial in partials)
            {
                var match = items.FirstOrDefault(item => item.IndexOf(partial, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null)
                    return match;
            }

            return null;
        }

        private static string Sanitise(string name) =>
            name.Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();

        private static void ValidateObjectName(string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Object name cannot be blank.");

            if (name.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '-')))
                throw new InvalidOperationException($"Invalid object name '{name}'.");
        }

        private static string ComputeHash(string input)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(input);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }

        private static async Task<int> ExecuteCountAsync(SqlConnection connection, string sql)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private async Task<int?> GetSystemUserIdByEmailAsync(SqlConnection connection, string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            command.Parameters.AddWithValue("@Email", email);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        private async Task<string?> GetEngagementRoleAsync(SqlConnection connection, int clientId, int userId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 EngagementRole FROM dbo.UserClientAssignments WHERE ClientID = @ClientID AND UserID = @UserID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@UserID", userId);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : result.ToString();
        }

        private async Task<string?> GetRunEngagementRoleAsync(SqlConnection connection, int runId, int userId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 uca.EngagementRole
FROM dbo.UserClientAssignments uca
INNER JOIN dbo.ValidationRuns vr ON vr.ClientID = uca.ClientID
WHERE vr.RunID = @RunID AND uca.UserID = @UserID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@UserID", userId);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : result.ToString();
        }

        private async Task<int?> GetClientIdForRunAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID AND RuleNumber = 62;";
            command.Parameters.AddWithValue("@RunID", runId);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        private async Task MarkPreviousRunsHistoricalAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET IsCurrent = 0 WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber AND IsCurrent = 1;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await command.ExecuteNonQueryAsync();
        }

        private async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 Status FROM dbo.Clients WHERE ClientID = @ClientID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            var result = await command.ExecuteScalarAsync();
            if (string.Equals(Convert.ToString(result), "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This engagement is archived and cannot accept new validation runs.");
        }

        private async Task<string?> GetLatestValidationHashAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber ORDER BY RunTimestamp DESC, RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : result.ToString();
        }

        private async Task<List<RunSignoffViewModel>> GetRunSignoffsAsync(SqlConnection connection, int runId, int? currentUserId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT rs.SignoffRole, rs.Comment, rs.SignedOffAt, u.Email,
       LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,''))) AS ReviewerName,
       CASE WHEN @CurrentUserID IS NOT NULL AND rs.ReviewerID = @CurrentUserID THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsCurrentUser
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.Users u ON u.UserID = rs.ReviewerID
WHERE rs.RunID = @RunID
ORDER BY CASE ISNULL(rs.SignoffRole,'') WHEN 'DataAnalyst' THEN 1 WHEN 'Manager' THEN 2 WHEN 'Director' THEN 3 ELSE 4 END, rs.SignedOffAt DESC;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@CurrentUserID", currentUserId.HasValue ? currentUserId.Value : DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync();
            var items = new List<RunSignoffViewModel>();
            while (await reader.ReadAsync())
            {
                items.Add(new RunSignoffViewModel
                {
                    SignoffRole = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Comment = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    SignedOffAt = reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2),
                    ReviewerEmail = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    ReviewerName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    IsCurrentUser = !reader.IsDBNull(5) && reader.GetBoolean(5)
                });
            }

            return items;
        }

        private async Task<int> ClearSignoffsAsync(SqlConnection connection, int runId)
        {
            await using var countCommand = connection.CreateConfiguredCommand();
            countCommand.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            countCommand.Parameters.AddWithValue("@RunID", runId);
            var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            await using var deleteCommand = connection.CreateConfiguredCommand();
            deleteCommand.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            deleteCommand.Parameters.AddWithValue("@RunID", runId);
            await deleteCommand.ExecuteNonQueryAsync();

            return count;
        }

        private async Task<bool> IsWorkspaceSavedAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM dbo.ValidationRuns
    WHERE RunID = @RunID
      AND (WorkspaceSavedAt IS NOT NULL OR EXISTS (
            SELECT 1
            FROM dbo.ReviewSignoffs rs
            WHERE rs.RunID = ValidationRuns.RunID
              AND rs.SignoffRole = 'DataAnalyst'))
) THEN 1 ELSE 0 END;";
            command.Parameters.AddWithValue("@RunID", runId);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }
    }
}
