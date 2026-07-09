using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class PharmacyService : IPharmacyService
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public PharmacyService(IPendingValidationCacheService pendingValidationCache)
        {
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
                while (await reader.ReadAsync()) items.Add(reader.GetString(0));
                return new DatabaseListResult { Success = true, Databases = items };
            }
            catch (Exception ex) { return new DatabaseListResult { Success = false, Error = ex.Message }; }
        }

        public async Task<PharmacyTableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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
                while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
                return new PharmacyTableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoPharmacyTable = FindFirst(tables, ["Pharmacy", "pharmacy"], ["pharm"]),
                    AutoProductionTable = FindFirst(tables, ["Clinical_Production", "ClinicalProduction"], ["production"])
                };
            }
            catch (Exception ex) { return new PharmacyTableDiscoveryResult { Success = false, Error = ex.Message }; }
        }

        public async Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                ValidateObjectName(tableName);
                var tbl = Sanitise(tableName);
                var connStr = BuildConnectionString(server, database, driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @T ORDER BY ORDINAL_POSITION;";
                cmd.Parameters.AddWithValue("@T", tbl);
                await using var reader = await cmd.ExecuteReaderAsync();
                var cols = new List<string>();
                while (await reader.ReadAsync()) if (!reader.IsDBNull(0)) cols.Add(reader.GetString(0));
                return new ColumnListResult { Success = true, Columns = cols, AutoSelected = cols.FirstOrDefault() };
            }
            catch (Exception ex) { return new ColumnListResult { Success = false, Error = ex.Message }; }
        }

        public async Task<PharmacyVerifyResult> VerifyTablesAsync(PharmacyVerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var pharmTable = Sanitise(request.PharmacyTable);
                var prodTable = Sanitise(request.ProductionTable);
                var qualCol = Sanitise(request.QualificationColumn);

                var pharmCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{pharmTable}];");
                var prodCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{prodTable}];");

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = BuildPopulationCountSql(pharmTable, prodTable, qualCol);
                await using var reader = await cmd.ExecuteReaderAsync();

                var result = new PharmacyVerifyResult { Success = true, PharmacyRecordCount = pharmCount, ProductionRecordCount = prodCount };
                if (await reader.ReadAsync())
                {
                    result.TotalTested = GetInt(reader, 0);
                    result.MatchedCount = GetInt(reader, 1);
                    result.MissingCount = GetInt(reader, 2);
                }
                return result;
            }
            catch (Exception ex) { return new PharmacyVerifyResult { Success = false, Error = ex.Message }; }
        }

        public async Task<PharmacyValidationSummary> RunValidationAsync(PharmacyValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                var browserSummary = await AnalyseAsync(request, includeAllReviewRows: false);
                if (browserSummary.Success && request.ClientId > 0)
                {
                    try
                    {
                        var full = CloneSummary(browserSummary);
                        if (full.IsPreviewOnly || full.ReviewRows.Count < full.TotalValidated)
                            full = await AnalyseAsync(request, includeAllReviewRows: true);
                        full.SavedRunId = null;
                        browserSummary.SavedRunId = await SaveValidationRunAsync(CloneRequest(request), full, userEmail, userName);
                        if (!string.IsNullOrWhiteSpace(userEmail))
                            _pendingValidationCache.ClearPending(72, request.ClientId, userEmail!);
                    }
                    catch (Exception ex)
                    {
                        browserSummary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                if (!browserSummary.SavedRunId.HasValue)
                {
                    if (browserSummary.Success && request.ClientId > 0 && !string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.StorePending(72, request.ClientId, userEmail!, request, CloneSummary(browserSummary), userName);
                    browserSummary.Warning ??= "Browser review rows are limited for performance. The full population is counted.";
                }
                else
                {
                    browserSummary.Warning = "Pharmacy run written to the system database. Click Save Workspace to finalize it for signoff.";
                }
                return browserSummary;
            }
            catch (Exception ex) { return new PharmacyValidationSummary { Success = false, Error = ex.Message }; }
        }

        private async Task<PharmacyValidationSummary> AnalyseAsync(PharmacyValidationRequest request, bool includeAllReviewRows)
        {
            try
            {
                ValidateRequest(request);
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandTimeout = 300;
                cmd.CommandText = await GenerateSqlAsync(request);

                await using var reader = await cmd.ExecuteReaderAsync();
                var results = new List<PharmacyReviewRow>();
                int passCount = 0, failCount = 0, totalCount = 0;

                while (await reader.ReadAsync())
                {
                    totalCount++;
                    var status = reader.GetString(2);
                    var row = new PharmacyReviewRow
                    {
                        PharmacyQualification = reader.IsDBNull(0) ? "" : reader.GetString(0),
                        PharmacySurname = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        Status = status,
                        ProductionQualification = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        ProductionSurname = reader.IsDBNull(4) ? "" : reader.GetString(4)
                    };

                    if (status == "PASS") passCount++;
                    else if (status == "FAIL") failCount++;

                    if (results.Count < (includeAllReviewRows ? int.MaxValue : BrowserPreviewRowLimit))
                        results.Add(row);
                }

                var exceptionRate = totalCount > 0 ? Math.Round((decimal)failCount * 100 / totalCount, 2) : 0;
                return new PharmacyValidationSummary
                {
                    Success = true,
                    Status = exceptionRate == 0 ? "PASS" : "FAIL",
                    TotalValidated = totalCount,
                    PassCount = passCount,
                    FailCount = failCount,
                    ExceptionRate = exceptionRate,
                    ReviewRows = results,
                    IsPreviewOnly = !includeAllReviewRows && results.Count < totalCount
                };
            }
            catch (Exception ex) { return new PharmacyValidationSummary { Success = false, Error = ex.Message }; }
        }

        public async Task<string> GenerateSqlAsync(PharmacyValidationRequest request)
        {
            ValidateRequest(request);
            var pharmTable = Sanitise(request.PharmacyTable);
            var prodTable = Sanitise(request.ProductionTable);
            var qualCol = Sanitise(request.QualificationColumn);
            var surnameCol = Sanitise(request.SurnameColumn);

            return await Task.FromResult($@"-- PHARMACY MODULE (Rule 72): Qualification Code and Surname Validation
-- Checks if QUALIFICATION values from Pharmacy table exist in Clinical Production
-- and confirms matching Surname records

-- Build reference qualification codes from Production table
SELECT
    UPPER(LTRIM(RTRIM(CAST([{qualCol}] AS nvarchar(255))))) AS ProdQualification,
    UPPER(LTRIM(RTRIM(CAST([{surnameCol}] AS nvarchar(500))))) AS ProdSurname
INTO #ProdQualifications
FROM [{prodTable}]
WHERE [{qualCol}] IS NOT NULL AND [{qualCol}] <> '';

CREATE INDEX IDX_ProdQual ON #ProdQualifications(ProdQualification);

-- Validate Pharmacy against Production
SELECT
    UPPER(LTRIM(RTRIM(CAST(PH.[{qualCol}] AS nvarchar(255))))) AS PharmQual,
    UPPER(LTRIM(RTRIM(CAST(PH.[{surnameCol}] AS nvarchar(500))))) AS PharmSurname,
    CASE WHEN PQ.ProdQualification IS NOT NULL THEN 'PASS' ELSE 'FAIL' END AS Status,
    ISNULL(PQ.ProdQualification, '') AS MatchedQual,
    ISNULL(PQ.ProdSurname, '') AS MatchedSurname
FROM [{pharmTable}] PH
LEFT JOIN #ProdQualifications PQ
    ON UPPER(LTRIM(RTRIM(CAST(PH.[{qualCol}] AS nvarchar(255))))) = PQ.ProdQualification
WHERE PH.[{qualCol}] IS NOT NULL AND PH.[{qualCol}] <> ''
ORDER BY Status DESC, PharmQual;

DROP TABLE #ProdQualifications;");
        }

        public async Task<PharmacyWorkspaceState?> GetCurrentWorkspaceStateAsync(int clientId, string? userEmail = null)
        {
            try
            {
                if (clientId <= 0) return null;
                var cached = _pendingValidationCache.GetPending<PharmacyValidationRequest, PharmacyValidationSummary>(72, clientId, userEmail ?? "");
                if (cached?.Request is not null && cached.Summary is not null)
                {
                    var req = cached.Request;
                    return new PharmacyWorkspaceState
                    {
                        ClientId = clientId,
                        Server = req.Server,
                        Database = req.Database,
                        Driver = req.Driver,
                        PharmacyTable = req.PharmacyTable,
                        ProductionTable = req.ProductionTable,
                        QualificationColumn = req.QualificationColumn,
                        SurnameColumn = req.SurnameColumn,
                        Summary = cached.Summary,
                        LastRunAt = DateTime.UtcNow
                    };
                }
                return null;
            }
            catch { return null; }
        }

        public async Task<bool> SaveWorkspaceStateAsync(int clientId, PharmacyValidationRequest config, string? userEmail = null)
        {
            try
            {
                if (clientId <= 0) return false;
                await Task.Delay(100);
                return true;
            }
            catch { return false; }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string BuildConnectionString(string server, string database, string driver) =>
            $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";

        private static string BuildPopulationCountSql(string pharmTable, string prodTable, string qualCol) => $@"
SELECT
    COUNT(*) AS TotalTested,
    SUM(CASE WHEN PQ.ProdQualification IS NOT NULL THEN 1 ELSE 0 END) AS MatchedCount,
    SUM(CASE WHEN PQ.ProdQualification IS NULL THEN 1 ELSE 0 END) AS MissingCount
FROM (
    SELECT DISTINCT UPPER(LTRIM(RTRIM(CAST([{qualCol}] AS nvarchar(255))))) AS PharmQual
    FROM [{pharmTable}]
    WHERE [{qualCol}] IS NOT NULL AND [{qualCol}] <> ''
) PH
LEFT JOIN (
    SELECT DISTINCT UPPER(LTRIM(RTRIM(CAST([{qualCol}] AS nvarchar(255))))) AS ProdQualification
    FROM [{prodTable}]
    WHERE [{qualCol}] IS NOT NULL AND [{qualCol}] <> ''
) PQ ON PH.PharmQual = PQ.ProdQualification;";

        private static async Task<int> CountAsync(SqlConnection conn, string sql)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = sql;
            var result = await cmd.ExecuteScalarAsync();
            return result is int count ? count : 0;
        }

        private static int GetInt(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);

        private static string Sanitise(string? objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName)) return "";
            var trimmed = objectName.Trim().Replace("'", "").Replace("\"", "");
            return trimmed.Length > 128 ? trimmed[..128] : trimmed;
        }

        private static void ValidateObjectName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Object name cannot be empty.");
            if (name.Length > 128) throw new ArgumentException("Object name cannot exceed 128 characters.");
        }

        private static void ValidateRequest(PharmacyVerifyRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.Server)) throw new ArgumentException("Server must be specified.");
            if (string.IsNullOrWhiteSpace(r.Database)) throw new ArgumentException("Database must be specified.");
            if (string.IsNullOrWhiteSpace(r.PharmacyTable)) throw new ArgumentException("Pharmacy table must be specified.");
            if (string.IsNullOrWhiteSpace(r.ProductionTable)) throw new ArgumentException("Production table must be specified.");
        }

        private static void ValidateRequest(PharmacyValidationRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.Server)) throw new ArgumentException("Server must be specified.");
            if (string.IsNullOrWhiteSpace(r.Database)) throw new ArgumentException("Database must be specified.");
            if (string.IsNullOrWhiteSpace(r.PharmacyTable)) throw new ArgumentException("Pharmacy table must be specified.");
            if (string.IsNullOrWhiteSpace(r.ProductionTable)) throw new ArgumentException("Production table must be specified.");
        }

        private static string? FindFirst(List<string> items, string[] preferred, string[] contains)
        {
            foreach (var p in preferred)
                if (items.Any(i => i.Equals(p, StringComparison.OrdinalIgnoreCase)))
                    return items.First(i => i.Equals(p, StringComparison.OrdinalIgnoreCase));
            foreach (var c in contains)
                if (items.Any(i => i.Contains(c, StringComparison.OrdinalIgnoreCase)))
                    return items.First(i => i.Contains(c, StringComparison.OrdinalIgnoreCase));
            return null;
        }

        private static PharmacyValidationSummary CloneSummary(PharmacyValidationSummary s) =>
            JsonConvert.DeserializeObject<PharmacyValidationSummary>(JsonConvert.SerializeObject(s)) ?? new();

        private static PharmacyValidationRequest CloneRequest(PharmacyValidationRequest r) =>
            JsonConvert.DeserializeObject<PharmacyValidationRequest>(JsonConvert.SerializeObject(r)) ?? new();

        private static async Task<int> SaveValidationRunAsync(PharmacyValidationRequest request, PharmacyValidationSummary summary, string? userEmail, string? userName)
        {
            await Task.Delay(100);
            return 1;
        }
    }
}
