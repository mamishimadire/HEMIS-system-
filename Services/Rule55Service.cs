using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule55Service : IRule55Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule55Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
        {
            _configuration = configuration;
            _pendingValidationCache = pendingValidationCache;
        }

        private static string BuildConnectionString(string server, string database, string driver) =>
            $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";

        private record PqmRow(string? Name, string? Type);

        // â"€â"€ Public API â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

        public async Task<DatabaseListResult> GetDatabasesAsync(string server, string driver)
        {
            try
            {
                var connStr = $"Server={server};Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT name FROM sys.databases " +
                    "WHERE name NOT IN ('master','tempdb','model','msdb') ORDER BY name", conn)
                    .WithLargeDataTimeout();
                using var reader = await cmd.ExecuteReaderAsync();
                var dbs = new List<string>();
                while (await reader.ReadAsync())
                    dbs.Add(reader.GetString(0));
                return new DatabaseListResult { Success = true, Databases = dbs };
            }
            catch (Exception ex)
            {
                return new DatabaseListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule55TableListResult> GetTablesAsync(string server, string database, string driver)
        {
            try
            {
                var connStr = BuildConnectionString(server, database, driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES " +
                    "WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME", conn)
                    .WithLargeDataTimeout();
                using var reader = await cmd.ExecuteReaderAsync();
                var tables = new List<string>();
                while (await reader.ReadAsync())
                    tables.Add(reader.GetString(0));

                return new Rule55TableListResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = tables.FirstOrDefault(t =>
                        t.Equals("dbo_STUD", StringComparison.OrdinalIgnoreCase)),
                    AutoQualTable = tables.FirstOrDefault(t =>
                        t.Equals("dbo_QUAL", StringComparison.OrdinalIgnoreCase)),
                    AutoPqmTable = tables.FirstOrDefault(t =>
                        t.IndexOf("PQM", StringComparison.OrdinalIgnoreCase) >= 0)
                };
            }
            catch (Exception ex)
            {
                return new Rule55TableListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<ColumnListResult> GetColumnsAsync(
            string server, string database, string driver, string tableName, string tableRole)
        {
            try
            {
                var connStr = BuildConnectionString(server, database, driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
                    "WHERE TABLE_NAME=@t ORDER BY ORDINAL_POSITION", conn)
                    .WithLargeDataTimeout();
                cmd.Parameters.AddWithValue("@t", tableName);
                using var reader = await cmd.ExecuteReaderAsync();
                var columns = new List<string>();
                while (await reader.ReadAsync())
                    columns.Add(reader.GetString(0));

                string? autoSelected = tableRole?.ToLowerInvariant() switch
                {
                    "stud_id"        => columns.FirstOrDefault(c => c.Equals("_007", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "stud_qual_code" => columns.FirstOrDefault(c => c.Equals("_001", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "stud_fulfilled" => columns.FirstOrDefault(c => c.Equals("_025", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_code"      => columns.FirstOrDefault(c => c.Equals("_001", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_name"      => columns.FirstOrDefault(c => c.Equals("_003", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_type"      => columns.FirstOrDefault(c => c.Equals("_005", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_approval"  => columns.FirstOrDefault(c => c.Equals("_004", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_qual_name"  => columns.FirstOrDefault(c => c.Equals("Authorised_Qualification_Name", StringComparison.OrdinalIgnoreCase) || c.Equals("AUTHORISED_QUALIFICATION_NAME", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_qual_type"  => columns.FirstOrDefault(c => c.Equals("HEQF_Qual_Type", StringComparison.OrdinalIgnoreCase) || c.Equals("HEQF_QUAL_TYPE", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    _                => columns.FirstOrDefault()
                };

                return new ColumnListResult
                {
                    Success = true,
                    Columns = columns,
                    AutoSelected = autoSelected
                };
            }
            catch (Exception ex)
            {
                return new ColumnListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule55VerifyResult> VerifyDataAsync(Rule55VerifyRequest request)
        {
            try
            {
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var st    = Sanitise(request.StudTable);
                var qt    = Sanitise(request.QualTable);
                var sfcol = Sanitise(request.StudFulfilledCol);
                var fval  = NormalizeFilterValue(request.StudFulfilledFilterValue, "W");

                var sql = $@"
SELECT
    (SELECT COUNT(*) FROM [{st}]) AS Stud_Total,
    (SELECT COUNT(*) FROM [{qt}]) AS Qual_Total,
    (SELECT COUNT(*) FROM [{st}]
     WHERE UPPER(LTRIM(RTRIM(CAST([{sfcol}] AS NVARCHAR(255))))) = '{fval}') AS Filtered_Total";

                using var cmd = new SqlCommand(sql, conn).WithLargeDataTimeout();
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new Rule55VerifyResult
                    {
                        Success       = true,
                        StudTotal     = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
                        QualTotal     = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                        FilteredTotal = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2)),
                        FilterColumn  = request.StudFulfilledCol,
                        FilterValue   = fval
                    };
                }
                return new Rule55VerifyResult { Success = false, Error = "No data returned" };
            }
            catch (Exception ex)
            {
                return new Rule55VerifyResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule55ValidationSummary> RunValidationAsync(
            Rule55ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var st    = Sanitise(request.StudTable);
                var qt    = Sanitise(request.QualTable);
                var si    = Sanitise(request.StudIdCol);
                var sqc   = Sanitise(request.StudQualCodeCol);
                var sfc   = Sanitise(request.StudFulfilledCol);
                var qc    = Sanitise(request.QualCodeCol);
                var qn    = Sanitise(request.QualNameCol);
                var qt2   = Sanitise(request.QualTypeCol);
                var qa    = Sanitise(request.QualApprovalCol);
                var fval  = NormalizeFilterValue(request.StudFulfilledFilterValue, "W");
                var approvalValue = NormalizeFilterValue(request.QualApprovalFilterValue, "A");

                // Load PQM rows for name+type matching (optional table)
                var pqm = new List<PqmRow>();
                if (!string.IsNullOrWhiteSpace(request.PqmTable) &&
                    !string.IsNullOrWhiteSpace(request.PqmQualNameColumn) &&
                    !string.IsNullOrWhiteSpace(request.PqmQualTypeColumn))
                {
                    var pt  = Sanitise(request.PqmTable);
                    var pn  = Sanitise(request.PqmQualNameColumn);
                    var pht = Sanitise(request.PqmQualTypeColumn);
                    using var pqmCmd = new SqlCommand($"SELECT [{pn}], [{pht}] FROM [{pt}]", conn).WithLargeDataTimeout();
                    using var pqmR = await pqmCmd.ExecuteReaderAsync();
                    while (await pqmR.ReadAsync())
                        pqm.Add(new PqmRow(
                            pqmR.IsDBNull(0) ? null : pqmR.GetValue(0)?.ToString(),
                            pqmR.IsDBNull(1) ? null : pqmR.GetValue(1)?.ToString()));
                }

                var sql = $@"
SELECT s.[{si}], s.[{sqc}], s.[{sfc}], q.[{qn}], q.[{qt2}], q.[{qa}]
FROM [{st}] s
LEFT JOIN [{qt}] q
  ON UPPER(LTRIM(RTRIM(CAST(s.[{sqc}] AS NVARCHAR(255)))))
   = UPPER(LTRIM(RTRIM(CAST(q.[{qc}] AS NVARCHAR(255)))))
WHERE UPPER(LTRIM(RTRIM(CAST(s.[{sfc}] AS NVARCHAR(255))))) = '{fval}'";

                var validationRows = new List<Rule55ValidationRow>();
                int rowNo = 0;

                using (var cmd = new SqlCommand(sql, conn).WithLargeDataTimeout())
                using (var r = await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        rowNo++;
                        var studentId      = r.IsDBNull(0) ? "" : r.GetValue(0)?.ToString() ?? "";
                        var qualCode       = r.IsDBNull(1) ? "" : r.GetValue(1)?.ToString() ?? "";
                        var fulfilledStatus = r.IsDBNull(2) ? "" : r.GetValue(2)?.ToString() ?? "";
                        var qualName       = r.IsDBNull(3) ? null : r.GetValue(3)?.ToString();
                        var qualType       = r.IsDBNull(4) ? null : r.GetValue(4)?.ToString();
                        var approvalStatus = r.IsDBNull(5) ? null : r.GetValue(5)?.ToString();

                        string result;
                        string? reason;

                        if (qualName == null)
                        {
                            result = "FAIL";
                            reason = "Qualification not found in dbo_QUAL";
                        }
                        else if (string.IsNullOrWhiteSpace(approvalStatus))
                        {
                            result = "FAIL";
                            reason = "Approval status missing (_004 is null)";
                        }
                        else if (!string.Equals(approvalStatus.Trim().ToUpperInvariant(), approvalValue, StringComparison.Ordinal))
                        {
                            result = "FAIL";
                            reason = $"Qualification not approved (_004='{approvalStatus.Trim()}', expected '{approvalValue}')";
                        }
                        else if (pqm.Count > 0)
                        {
                            var nameNorm = qualName?.Trim().ToUpperInvariant() ?? "";
                            var typeNorm = qualType?.Trim().ToUpperInvariant() ?? "";
                            var tripleMatch = pqm.FirstOrDefault(p =>
                                string.Equals(p.Name?.Trim().ToUpperInvariant() ?? "", nameNorm, StringComparison.Ordinal) &&
                                string.Equals(p.Type?.Trim().ToUpperInvariant() ?? "", typeNorm, StringComparison.Ordinal));
                            if (tripleMatch != null)
                            {
                                result = "PASS";
                                reason = null;
                            }
                            else
                            {
                                var nameMatch = pqm.Any(p =>
                                    string.Equals(p.Name?.Trim().ToUpperInvariant() ?? "", nameNorm, StringComparison.Ordinal));
                                result = "FAIL";
                                reason = nameMatch
                                    ? $"Name matched PQM but HEQF type mismatch — QUAL._005: '{qualType?.Trim()}'"
                                    : $"Qualification name not found in PQM register — QUAL._003: '{qualName?.Trim()}'";
                            }
                        }
                        else
                        {
                            result = "PASS";
                            reason = null;
                        }

                        validationRows.Add(new Rule55ValidationRow
                        {
                            ValidationNumber  = rowNo,
                            StudentId         = studentId,
                            QualCode          = qualCode,
                            FulfilledStatus   = fulfilledStatus,
                            QualName          = qualName,
                            QualType          = qualType,
                            QualApprovalStatus = approvalStatus,
                            PqmQualName = pqm.Count > 0 && qualName != null
                                ? (pqm.FirstOrDefault(p => string.Equals(p.Name?.Trim().ToUpperInvariant() ?? "", qualName.Trim().ToUpperInvariant(), StringComparison.Ordinal))?.Name?.Trim())
                                : null,
                            PqmQualType = pqm.Count > 0 && qualName != null
                                ? (pqm.FirstOrDefault(p => string.Equals(p.Name?.Trim().ToUpperInvariant() ?? "", qualName.Trim().ToUpperInvariant(), StringComparison.Ordinal))?.Type?.Trim())
                                : null,
                            NameMatch = pqm.Count == 0 || (qualName != null && pqm.Any(p => string.Equals(p.Name?.Trim().ToUpperInvariant() ?? "", qualName.Trim().ToUpperInvariant(), StringComparison.Ordinal))),
                            TypeMatch = result == "PASS",
                            ValidationResult  = result,
                            ExceptionReason   = reason
                        });
                    }
                }

                var total     = validationRows.Count;
                var passCount = validationRows.Count(row => row.ValidationResult == "PASS");
                var failCount = validationRows.Count(row => row.ValidationResult == "FAIL");
                var rate      = total > 0 ? Math.Round((decimal)failCount / total * 100, 2) : 0;

                var exceptions = validationRows
                    .Where(row => row.ValidationResult == "FAIL")
                    .Select(row => new Rule55ExceptionRecord
                    {
                        ValidationNumber   = row.ValidationNumber,
                        StudentId          = row.StudentId,
                        QualCode           = row.QualCode,
                        FulfilledStatus    = row.FulfilledStatus,
                        QualName           = row.QualName,
                        QualType           = row.QualType,
                        QualApprovalStatus = row.QualApprovalStatus,
                        PqmQualName        = row.PqmQualName,
                        PqmQualType        = row.PqmQualType,
                        NameMatch          = row.NameMatch,
                        TypeMatch          = row.TypeMatch,
                        ValidationResult   = row.ValidationResult,
                        ExceptionReason    = row.ExceptionReason ?? ""
                    })
                    .ToList();

                var summary = new Rule55ValidationSummary
                {
                    Success          = true,
                    TotalValidated   = total,
                    PassCount        = passCount,
                    FailCount        = failCount,
                    ExceptionRate    = rate,
                    Status           = failCount == 0 ? "PASS" : "FAIL",
                    Timestamp        = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Database         = request.Database,
                    StudTable        = request.StudTable,
                    QualTable        = request.QualTable,
                    StudIdCol        = request.StudIdCol,
                    StudQualCodeCol  = request.StudQualCodeCol,
                    StudFulfilledCol        = request.StudFulfilledCol,
                    StudFulfilledFilterValue = fval,
                    QualApprovalFilterValue = approvalValue,
                    QualCodeCol             = request.QualCodeCol,
                    QualNameCol      = request.QualNameCol,
                    QualTypeCol      = request.QualTypeCol,
                    QualApprovalCol  = request.QualApprovalCol,
                    PqmTable         = request.PqmTable,
                    PqmQualNameColumn = request.PqmQualNameColumn,
                    PqmQualTypeColumn = request.PqmQualTypeColumn,
                    ClientId         = request.ClientId,
                    ValidationRows   = validationRows,
                    Exceptions       = exceptions
                };

                if (request.ClientId > 0)
                {
                    await using var systemConnection = await OpenSystemConnectionAsync();
                    var systemUserId = await GetSystemUserIdByEmailAsync(systemConnection, userEmail);
                    if (!systemUserId.HasValue)
                        throw new InvalidOperationException("Current user is not available in the system database.");

                    await EnsureClientNotArchivedAsync(systemConnection, request.ClientId);
                    await ClearRuleSignoffsAsync(systemConnection, request.ClientId, 55);
                    await MarkPreviousRunsHistoricalAsync(systemConnection, request.ClientId, 55);

                    var runId = await InsertValidationRunAsync(systemConnection, request, summary, systemUserId.Value, userName);
                    summary.SavedRunId = runId;

                    await using var update = systemConnection.CreateConfiguredCommand();
                    update.CommandText = @"
UPDATE dbo.ValidationRuns
SET ResultsJSON = @ResultsJSON
WHERE RunID = @RunID;";
                    update.Parameters.AddWithValue("@RunID", runId);
                    update.Parameters.AddWithValue("@ResultsJSON",
                        ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
                    await update.ExecuteNonQueryAsync();
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule55ValidationSummary { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule55WorkspaceSaveResult> SaveWorkspaceAsync(
            Rule55ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                    return new Rule55WorkspaceSaveResult { Success = false, Error = "Run validation before saving the workspace." };

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                    return new Rule55WorkspaceSaveResult { Success = false, Error = "The saved workspace could not be found for this engagement." };

                await EnsureClientNotArchivedAsync(connection, request.ClientId);

                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                    return new Rule55WorkspaceSaveResult { Success = false, Error = "Current user is not available in the system database." };

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                var previousHash    = await GetValidationRecordHashAsync(connection, request.RunId.Value);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer          = @HemisServer,
    AuditDatabase        = @AuditDatabase,
    StudTable            = @StudTable,
    DeceasedTable        = @DeceasedTable,
    StudColumn           = @StudColumn,
    DeceasedColumn       = @DeceasedColumn,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    WorkspaceSavedAt     = GETDATE(),
    PreviousHash         = @PreviousHash,
    RecordHash           = @RecordHash,
    Status               = 'Needs Review'
WHERE RunID   = @RunID
  AND ClientID = @ClientID;";
                command.Parameters.AddWithValue("@RunID",               request.RunId.Value);
                command.Parameters.AddWithValue("@ClientID",            request.ClientId);
                command.Parameters.AddWithValue("@HemisServer",         request.Server);
                command.Parameters.AddWithValue("@AuditDatabase",       request.Database);
                command.Parameters.AddWithValue("@StudTable",           request.StudTable);
                command.Parameters.AddWithValue("@DeceasedTable",       request.QualTable);
                command.Parameters.AddWithValue("@StudColumn",          request.StudFulfilledCol);
                command.Parameters.AddWithValue("@DeceasedColumn",      NormalizeFilterValue(request.StudFulfilledFilterValue, "W"));
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash",        (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash(
                    $"WorkspaceSave|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|" +
                    $"{request.StudTable}|{request.QualTable}|" +
                    $"{request.StudFulfilledFilterValue}|{request.QualApprovalFilterValue}|" +
                    $"{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail);
                return new Rule55WorkspaceSaveResult
                {
                    Success = true,
                    Message = clearedSignoffs > 0
                        ? "Workspace saved. Existing signoffs were removed and the run must be reviewed again."
                        : "Workspace saved and marked for review again.",
                    SignoffsCleared     = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace           = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule55WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule55WorkspaceSaveResult> BeginWorkspaceEditAsync(
            int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                    return new Rule55WorkspaceSaveResult { Success = false, Error = "Current user is not available in the system database." };

                var clientId = await GetClientIdForRunAsync(runId);
                if (!clientId.HasValue)
                    return new Rule55WorkspaceSaveResult { Success = false, Error = "Saved workspace was not found." };

                await EnsureClientNotArchivedAsync(connection, clientId.Value);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash    = await GetValidationRecordHashAsync(connection, runId);

                await using (var markEdit = connection.CreateConfiguredCommand())
                {
                    markEdit.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    WorkspaceSavedAt     = NULL,
    PreviousHash         = @PreviousHash,
    RecordHash           = @RecordHash,
    Status               = 'Needs Review'
WHERE RunID = @RunID;";
                    markEdit.Parameters.AddWithValue("@RunID", runId);
                    markEdit.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? reviewerEmail);
                    markEdit.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                    markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash(
                        $"WorkspaceEdit|{runId}|{reviewerName ?? reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                    await markEdit.ExecuteNonQueryAsync();
                }

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail);
                return new Rule55WorkspaceSaveResult
                {
                    Success             = true,
                    Message             = clearedSignoffs > 0
                        ? "Editing has begun. Existing signoffs were removed so the workspace must be reviewed again."
                        : "Editing has begun.",
                    SignoffsCleared     = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace           = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule55WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        public async Task<Rule55WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(
            int clientId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID,
    vr.ClientID,
    ISNULL(vr.HemisServer,   '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable,     '') AS StudTable,
    ISNULL(vr.DeceasedTable, '') AS QualTable,
    ISNULL(vr.StudColumn,      '') AS StudFulfilledCol,
    ISNULL(vr.DeceasedColumn,  '') AS StudFulfilledFilterValue,
    ISNULL(vr.Status,          '') AS Status,
    ISNULL(vr.LastEditedByUserName, '') AS LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID   = @ClientID
  AND vr.RuleNumber = 55
ORDER BY vr.IsCurrent DESC, vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summaryJson = reader.IsDBNull(11) ? null : reader.GetString(11);
            var summary = DeserializeSummary(summaryJson);
            if (summary != null)
                ApplyBrowserPreview(summary);

            // Restore filter value: prefer JSON payload, fall back to DB column
            var dbFilterVal = reader.IsDBNull(7) ? "W" : reader.GetString(7);
            var restoredFilterValue = summary?.StudFulfilledFilterValue is { Length: > 0 } v ? v : (dbFilterVal.Length > 0 ? dbFilterVal : "W");

            var workspace = new Rule55WorkspaceStateViewModel
            {
                RunId         = reader.GetInt32(0),
                ClientId      = reader.GetInt32(1),
                Server        = reader.GetString(2),
                Database      = reader.GetString(3),
                Driver        = "ODBC Driver 17 for SQL Server",
                StudTable     = reader.GetString(4),
                QualTable     = reader.GetString(5),
                StudFulfilledFilterValue = restoredFilterValue,
                CurrentStatus = reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9)  ? null : reader.GetString(9),
                LastEditedAt         = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Summary              = summary
            };

            if (summary != null)
            {
                workspace.StudIdCol              = summary.StudIdCol;
                workspace.StudQualCodeCol        = summary.StudQualCodeCol;
                workspace.StudFulfilledCol       = summary.StudFulfilledCol;
                workspace.StudFulfilledFilterValue = restoredFilterValue;
                workspace.QualCodeCol            = summary.QualCodeCol;
                workspace.QualNameCol            = summary.QualNameCol;
                workspace.QualTypeCol            = summary.QualTypeCol;
                workspace.QualApprovalCol        = summary.QualApprovalCol;
                workspace.QualApprovalFilterValue = summary.QualApprovalFilterValue;
                workspace.PqmTable          = summary.PqmTable;
                workspace.PqmQualNameColumn  = summary.PqmQualNameColumn;
                workspace.PqmQualTypeColumn  = summary.PqmQualTypeColumn;
            }

            await reader.CloseAsync();

            int? currentUserId = null;
            if (!string.IsNullOrWhiteSpace(currentUserEmail))
            {
                currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);
                if (currentUserId.HasValue)
                    workspace.CurrentUserEngagementRole =
                        await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? "";
            }

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId!.Value, currentUserId);
            workspace.HasDataAnalystSignoff = signoffs.Any(s =>
                string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            var currentRoleSignoff = signoffs.FirstOrDefault(s =>
                HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(
                    s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff   = currentRoleSignoff != null;
            workspace.CurrentUserSignoffComment = currentRoleSignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved          = await IsWorkspaceSavedAsync(connection, workspace.RunId!.Value);

            if (workspace.Summary != null)
                workspace.Summary.SavedRunId = workspace.RunId;

            if (string.IsNullOrWhiteSpace(workspace.CurrentStatus))
                workspace.CurrentStatus = workspace.Summary?.Status ?? "";

            return workspace;
        }

        public async Task<Rule55RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summaryJson = reader.IsDBNull(6) ? null : reader.GetString(6);
            var summary = DeserializeSummary(summaryJson);
            if (summary == null)
                return null;

            var viewModel = new Rule55RunReviewViewModel
            {
                RunId          = reader.GetInt32(0),
                ClientId       = reader.GetInt32(1),
                IsCurrentRun   = !reader.IsDBNull(2) && reader.GetBoolean(2),
                EngagementName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                MaconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SourceServer   = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Summary        = summary
            };

            await reader.CloseAsync();

            int? currentUserId = null;
            if (!string.IsNullOrWhiteSpace(currentUserEmail))
            {
                currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);
                if (currentUserId.HasValue)
                    viewModel.CurrentUserEngagementRole =
                        await GetEngagementRoleAsync(connection, viewModel.ClientId, currentUserId.Value) ?? "";
            }

            viewModel.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            viewModel.HasDataAnalystSignoff = viewModel.Signoffs.Any(s =>
                string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            return viewModel;
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue)
                throw new InvalidOperationException("Reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(runId);
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
IF EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND ReviewerID = @ReviewerID)
BEGIN
    UPDATE dbo.ReviewSignoffs
    SET SignoffRole = @SignoffRole, ReviewType = 'Final', Comment = @Comment, SignedOffAt = GETDATE()
    WHERE RunID = @RunID AND ReviewerID = @ReviewerID;
END
ELSE
BEGIN
    INSERT INTO dbo.ReviewSignoffs (ClientID, RunID, ReviewerID, SignoffRole, ReviewType, Comment, SignedOffAt)
    VALUES (@ClientID, @RunID, @ReviewerID, @SignoffRole, 'Final', @Comment, GETDATE());
END";
            command.Parameters.AddWithValue("@ClientID",    clientId.Value);
            command.Parameters.AddWithValue("@RunID",       runId);
            command.Parameters.AddWithValue("@ReviewerID",  reviewerId.Value);
            command.Parameters.AddWithValue("@SignoffRole", engagementRole!);
            command.Parameters.AddWithValue("@Comment",
                string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim());
            await command.ExecuteNonQueryAsync();

            await UpdateRunStatusFromSignoffsAsync(connection, runId);
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue)
                throw new InvalidOperationException("Reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("Validation run was not found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(
                connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0) return;
        }

        public string GenerateSql(Rule55ValidationRequest request)
        {
            var st   = request.StudTable;
            var qt   = request.QualTable;
            var si   = request.StudIdCol;
            var sqc  = request.StudQualCodeCol;
            var sfc  = request.StudFulfilledCol;
            var qc   = request.QualCodeCol;
            var qn   = request.QualNameCol;
            var qt2  = request.QualTypeCol;
            var qa   = request.QualApprovalCol;
            var fval = NormalizeFilterValue(request.StudFulfilledFilterValue, "W");
            var approvalValue = NormalizeFilterValue(request.QualApprovalFilterValue, "A");

            var hasPqm = !string.IsNullOrWhiteSpace(request.PqmTable) &&
                         !string.IsNullOrWhiteSpace(request.PqmQualNameColumn) &&
                         !string.IsNullOrWhiteSpace(request.PqmQualTypeColumn);
            var pt  = hasPqm ? Sanitise(request.PqmTable) : "";
            var pn  = hasPqm ? Sanitise(request.PqmQualNameColumn) : "";
            var pht = hasPqm ? Sanitise(request.PqmQualTypeColumn) : "";
            var pqmSqlClause = hasPqm
                ? $"        WHEN NOT EXISTS (\r\n            SELECT 1 FROM [{pt}] p\r\n            WHERE UPPER(LTRIM(RTRIM(CAST(p.[{pn}] AS NVARCHAR(500))))) = UPPER(LTRIM(RTRIM(CAST(q.[{qn}] AS NVARCHAR(500)))))\r\n              AND UPPER(LTRIM(RTRIM(CAST(p.[{pht}] AS NVARCHAR(100))))) = UPPER(LTRIM(RTRIM(CAST(q.[{qt2}] AS NVARCHAR(100)))))\r\n        ) THEN 'FAIL - PQM mismatch (_003/_005 not found in PQM register)'"
                : "";

            return $@"-- ============================================================================
-- HEMIS RULE 55  -  GRADUATE FULFILLED-STATUS VALIDATION
-- Generated  : {DateTime.Now:yyyy-MM-dd HH:mm:ss}
-- DHET Â§1.5  : Students coded '{fval}' are treated identically to 'F' graduates
-- Filter     : {st}.[{sfc}] = '{fval}'
-- ============================================================================
-- TABLES
--   {st}  : [{si}] (student ID), [{sqc}] (qualification code), [{sfc}] (fulfilled status = '{fval}')
--   {qt}  : [{qc}] (qualification code), [{qn}] (name), [{qt2}] (type), [{qa}] (approval)
-- FILTERS
--   {qt}.[{qa}] = '{approvalValue}'
--
-- POPULATION:  {st}.[{sfc}] = '{fval}'
-- JOIN:         {st}.[{sqc}] = {qt}.[{qc}]  (LEFT JOIN  -  captures missing quals)
--
-- PASS:  QUAL row found AND [{qa}] = '{approvalValue}'
-- FAIL:  QUAL not found | [{qa}] is null/empty | [{qa}] <> '{approvalValue}'
-- ============================================================================

-- Step 1: Filtered students joined to dbo_QUAL
SELECT
    s.[{si}]  AS Student_ID,
    s.[{sqc}] AS Qual_Code,
    s.[{sfc}] AS Fulfilled_Status,
    q.[{qn}]  AS Qual_Name,
    q.[{qt2}] AS Qual_Type,
    q.[{qa}]  AS Approval_Status,
    CASE
        WHEN q.[{qc}] IS NULL THEN 'FAIL  -  Qualification not found in {qt}'
        WHEN q.[{qa}] IS NULL OR LTRIM(RTRIM(CAST(q.[{qa}] AS NVARCHAR(10)))) = ''
            THEN 'FAIL  -  Approval status missing ({qa} is null)'
        WHEN UPPER(LTRIM(RTRIM(CAST(q.[{qa}] AS NVARCHAR(10))))) <> '{approvalValue}'
            THEN 'FAIL – Qualification not approved ({qa} <> {approvalValue})'
        {pqmSqlClause}
        ELSE 'PASS'
    END AS Validation_Result
FROM [{st}] s
LEFT JOIN [{qt}] q
  ON UPPER(LTRIM(RTRIM(CAST(s.[{sqc}] AS NVARCHAR(255)))))
   = UPPER(LTRIM(RTRIM(CAST(q.[{qc}] AS NVARCHAR(255)))))
WHERE UPPER(LTRIM(RTRIM(CAST(s.[{sfc}] AS NVARCHAR(255))))) = '{fval}'
ORDER BY s.[{si}];

-- Step 2: Summary
SELECT
    COUNT(*) AS Total_Filtered,
    SUM(CASE WHEN q.[{qc}] IS NOT NULL AND UPPER(LTRIM(RTRIM(CAST(q.[{qa}] AS NVARCHAR(10))))) = '{approvalValue}' THEN 1 ELSE 0 END) AS Pass_Count,
    SUM(CASE WHEN q.[{qc}] IS NULL OR ISNULL(LTRIM(RTRIM(CAST(q.[{qa}] AS NVARCHAR(10)))), '') = '' OR UPPER(LTRIM(RTRIM(CAST(q.[{qa}] AS NVARCHAR(10))))) <> '{approvalValue}' THEN 1 ELSE 0 END) AS Fail_Count
FROM [{st}] s
LEFT JOIN [{qt}] q
  ON UPPER(LTRIM(RTRIM(CAST(s.[{sqc}] AS NVARCHAR(255)))))
   = UPPER(LTRIM(RTRIM(CAST(q.[{qc}] AS NVARCHAR(255)))))
WHERE UPPER(LTRIM(RTRIM(CAST(s.[{sfc}] AS NVARCHAR(255))))) = '{fval}';

-- Step 3: Exceptions only
SELECT
    s.[{si}]  AS Student_ID,
    s.[{sqc}] AS Qual_Code,
    s.[{sfc}] AS Fulfilled_Status,
    q.[{qn}]  AS Qual_Name,
    q.[{qt2}] AS Qual_Type,
    q.[{qa}]  AS Approval_Status
FROM [{st}] s
LEFT JOIN [{qt}] q
  ON UPPER(LTRIM(RTRIM(CAST(s.[{sqc}] AS NVARCHAR(255)))))
   = UPPER(LTRIM(RTRIM(CAST(q.[{qc}] AS NVARCHAR(255)))))
WHERE UPPER(LTRIM(RTRIM(CAST(s.[{sfc}] AS NVARCHAR(255))))) = '{fval}'
  AND (
        q.[{qc}] IS NULL
        OR ISNULL(LTRIM(RTRIM(CAST(q.[{qa}] AS NVARCHAR(10)))), '') = ''
        OR UPPER(LTRIM(RTRIM(CAST(q.[{qa}] AS NVARCHAR(10))))) <> '{approvalValue}'
      )
ORDER BY s.[{si}];
-- ============================================================================
-- END OF RULE 55 GRADUATE W-CODE VALIDATION
-- ============================================================================
";
        }

        // â"€â"€ Private helpers â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

        private async Task<int> InsertValidationRunAsync(
            SqlConnection connection,
            Rule55ValidationRequest request,
            Rule55ValidationSummary summary,
            int systemUserId,
            string? userName)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
INSERT INTO dbo.ValidationRuns
(ClientID, UserID, HemisServer, AuditDatabase, StudTable, DeceasedTable, StudColumn, DeceasedColumn,
 RuleNumber, RuleName, Status, RunTimestamp, TotalRecords, PassCount, FailCount, ExceptionRate,
 ExceptionsJSON, ResultsJSON, RunByUserName, LastEditedByUserName, LastEditedAt, PreviousHash, RecordHash, IsCurrent)
VALUES
(@ClientID, @UserID, @HemisServer, @AuditDatabase, @StudTable, @DeceasedTable, @StudColumn, @DeceasedColumn,
 @RuleNumber, @RuleName, @Status, GETDATE(), @TotalRecords, @PassCount, @FailCount, @ExceptionRate,
 @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            command.Parameters.AddWithValue("@ClientID",      request.ClientId);
            command.Parameters.AddWithValue("@UserID",        systemUserId);
            command.Parameters.AddWithValue("@HemisServer",   request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable",     request.StudTable);
            command.Parameters.AddWithValue("@DeceasedTable", request.QualTable);
            command.Parameters.AddWithValue("@StudColumn",    request.StudFulfilledCol);
            command.Parameters.AddWithValue("@DeceasedColumn", "");
            command.Parameters.AddWithValue("@RuleNumber",    55);
            command.Parameters.AddWithValue("@RuleName",      "Graduate W-Code Validation");
            command.Parameters.AddWithValue("@Status",        summary.FailCount == 0 ? "Pass" : "Fail");
            command.Parameters.AddWithValue("@TotalRecords",  summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount",     summary.PassCount);
            command.Parameters.AddWithValue("@FailCount",     summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@ExceptionsJSON",
                ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary.Exceptions)));
            command.Parameters.AddWithValue("@ResultsJSON",
                ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? DBNull.Value);

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 55);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var value = await command.ExecuteScalarAsync();
            var runId = Convert.ToInt32(value);

            await using var hashCmd = connection.CreateConfiguredCommand();
            hashCmd.CommandText = @"UPDATE dbo.ValidationRuns SET RecordHash = @RecordHash WHERE RunID = @RunID;";
            hashCmd.Parameters.AddWithValue("@RunID", runId);
            hashCmd.Parameters.AddWithValue("@RecordHash", ComputeHash(
                $"ValidationRun|{runId}|{request.ClientId}|{systemUserId}|{summary.Status}|" +
                $"{summary.TotalValidated}|{summary.PassCount}|{summary.FailCount}|{summary.ExceptionRate}|" +
                $"{summary.Timestamp}|{request.StudFulfilledFilterValue}|{request.QualApprovalFilterValue}|{previousHash}"));
            await hashCmd.ExecuteNonQueryAsync();
            return runId;
        }

        private async Task<int> ClearSignoffsAndFlagForReviewAsync(SqlConnection connection, int runId)
        {
            await using var cnt = connection.CreateConfiguredCommand();
            cnt.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            cnt.Parameters.AddWithValue("@RunID", runId);
            var existingCount = Convert.ToInt32(await cnt.ExecuteScalarAsync());

            await using var del = connection.CreateConfiguredCommand();
            del.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            del.Parameters.AddWithValue("@RunID", runId);
            await del.ExecuteNonQueryAsync();

            await using var upd = connection.CreateConfiguredCommand();
            upd.CommandText = "UPDATE dbo.ValidationRuns SET Status = 'Needs Review' WHERE RunID = @RunID;";
            upd.Parameters.AddWithValue("@RunID", runId);
            await upd.ExecuteNonQueryAsync();

            return existingCount;
        }

        private async Task UpdateRunStatusFromSignoffsAsync(SqlConnection connection, int runId)
        {
            var hasAll = await HasAllRequiredSignoffsAsync(connection, runId);
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "UPDATE dbo.ValidationRuns SET Status = @Status WHERE RunID = @RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@Status", hasAll ? "Reviewed and Completed" : "Needs Review");
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<bool> HasAllRequiredSignoffsAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'DataAnalyst') THEN 1 ELSE 0 END AS HasDA,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Manager')     THEN 1 ELSE 0 END AS HasMgr,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Director')    THEN 1 ELSE 0 END AS HasDir;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return false;
            return (!r.IsDBNull(0) && r.GetInt32(0) == 1) &&
                   (!r.IsDBNull(1) && r.GetInt32(1) == 1) &&
                   (!r.IsDBNull(2) && r.GetInt32(2) == 1);
        }

        private async Task<int> ClearRuleSignoffsAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var cnt = connection.CreateConfiguredCommand();
            cnt.CommandText = @"SELECT COUNT(1) FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.ValidationRuns vr ON vr.RunID = rs.RunID
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = @RuleNumber;";
            cnt.Parameters.AddWithValue("@ClientID",   clientId);
            cnt.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var count = Convert.ToInt32(await cnt.ExecuteScalarAsync());

            await using var del = connection.CreateConfiguredCommand();
            del.CommandText = @"DELETE rs FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.ValidationRuns vr ON vr.RunID = rs.RunID
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = @RuleNumber;";
            del.Parameters.AddWithValue("@ClientID",   clientId);
            del.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await del.ExecuteNonQueryAsync();
            return count;
        }

        private async Task MarkPreviousRunsHistoricalAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"UPDATE dbo.ValidationRuns SET IsCurrent = 0
WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber AND IsCurrent = 1;";
            cmd.Parameters.AddWithValue("@ClientID",   clientId);
            cmd.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<List<RunSignoffViewModel>> GetRunSignoffsAsync(
            SqlConnection connection, int runId, int? currentUserId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
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
ORDER BY CASE ISNULL(rs.SignoffRole,'')
           WHEN 'DataAnalyst' THEN 1 WHEN 'Manager' THEN 2 WHEN 'Director' THEN 3 ELSE 4 END,
         rs.SignedOffAt DESC;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@CurrentUserID", currentUserId.HasValue ? currentUserId.Value : DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            var list = new List<RunSignoffViewModel>();
            while (await r.ReadAsync())
            {
                list.Add(new RunSignoffViewModel
                {
                    Id            = r.GetInt32(0),
                    SignoffRole   = r.IsDBNull(1) ? "" : r.GetString(1),
                    ReviewerName  = r.IsDBNull(2) ? "" : r.GetString(2),
                    ReviewerEmail = r.IsDBNull(3) ? "" : r.GetString(3),
                    Comment       = r.IsDBNull(4) ? "" : r.GetString(4),
                    SignedOffAt   = r.IsDBNull(5) ? DateTime.UtcNow : r.GetDateTime(5),
                    IsCurrentUser = !r.IsDBNull(6) && r.GetBoolean(6)
                });
            }
            return list;
        }

        private async Task<int?> GetSystemUserIdByEmailAsync(SqlConnection connection, string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            cmd.Parameters.AddWithValue("@Email", email);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToInt32(v);
        }

        private async Task<string?> GetEngagementRoleAsync(SqlConnection connection, int clientId, int userId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"SELECT TOP 1 EngagementRole FROM dbo.UserClientAssignments
WHERE ClientID = @ClientID AND UserID = @UserID;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            cmd.Parameters.AddWithValue("@UserID",   userId);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToString(v);
        }

        private static async Task<bool> IsWorkspaceSavedAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"SELECT CASE WHEN EXISTS (
    SELECT 1 FROM dbo.ValidationRuns
    WHERE RunID = @RunID
      AND (WorkspaceSavedAt IS NOT NULL
           OR EXISTS (SELECT 1 FROM dbo.ReviewSignoffs rs
                      WHERE rs.RunID = ValidationRuns.RunID AND rs.SignoffRole = 'DataAnalyst'))
) THEN 1 ELSE 0 END;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        private async Task<bool> HasSignoffRoleAsync(SqlConnection connection, int runId, string signoffRole)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole=@SignoffRole;";
            cmd.Parameters.AddWithValue("@RunID",       runId);
            cmd.Parameters.AddWithValue("@SignoffRole", signoffRole);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        private async Task<string?> GetValidationRecordHashAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToString(v);
        }

        private async Task<string?> GetLatestValidationHashAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"SELECT TOP 1 RecordHash FROM dbo.ValidationRuns
WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber AND RecordHash IS NOT NULL
ORDER BY RunTimestamp DESC, RunID DESC;";
            cmd.Parameters.AddWithValue("@ClientID",   clientId);
            cmd.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToString(v);
        }

        private async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 Status FROM dbo.Clients WHERE ClientID = @ClientID;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            var status = Convert.ToString(await cmd.ExecuteScalarAsync());
            if (string.Equals(status, "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Archived engagements are read-only.");
        }

        private async Task<SqlConnection> OpenSystemConnectionAsync()
        {
            var server   = _configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = _configuration["SystemDatabase:Name"] ?? "HEMISBaseSystem";
            var trust    = _configuration.GetValue("SystemDatabase:TrustServerCertificate", true);

            var builder = new SqlConnectionStringBuilder
            {
                DataSource             = server,
                InitialCatalog         = database,
                IntegratedSecurity     = true,
                TrustServerCertificate = trust,
                Encrypt                = false,
                ConnectTimeout         = 180
            };

            var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static Rule55ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                return JsonConvert.DeserializeObject<Rule55ValidationSummary>(decoded);
            }
            catch { return null; }
        }

        private static void ApplyBrowserPreview(Rule55ValidationSummary summary)
        {
            summary.ValidationRows = summary.ValidationRows.Take(BrowserPreviewRowLimit).ToList();
            summary.Exceptions     = summary.Exceptions.Take(BrowserPreviewRowLimit).ToList();
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager",     StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director",    StringComparison.OrdinalIgnoreCase);

        private static string NormalizeFilterValue(string? value, string defaultValue) =>
            string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim().ToUpperInvariant();

        private static string Sanitise(string? name) =>
            Regex.Replace((name ?? "").Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim(),
                @"[^\w\-\. ]", "");

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }
    }
}

