using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule38Service : IRule38Service
    {
        private const int BrowserPreviewRowLimit = 50;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule38Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
        {
            _configuration = configuration;
            _pendingValidationCache = pendingValidationCache;
        }

        private static string BuildConnectionString(string server, string database, string driver)
        {
            if (server.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase))
            {
                var pipeName = ResolveLocalDbPipe(server);
                if (pipeName != null)
                    return $"Server={pipeName};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=30;";
            }
            return $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";
        }

        private static string? ResolveLocalDbPipe(string server)
        {
            try
            {
                var instance = server.Contains('\\') ? server.Split('\\').Last().Trim() : "MSSQLLocalDB";

                // Start the instance (idempotent — does nothing if already running)
                using (var startP = Process.Start(new ProcessStartInfo("sqllocaldb", $"start \"{instance}\"")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                })!)
                {
                    startP.WaitForExit(8000);
                }

                // Get the named pipe path from sqllocaldb info
                using var infoP = Process.Start(new ProcessStartInfo("sqllocaldb", $"info \"{instance}\"")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                })!;
                var output = infoP.StandardOutput.ReadToEnd();
                infoP.WaitForExit(3000);

                var match = System.Text.RegularExpressions.Regex.Match(
                    output, @"Instance pipe name:\s*(np:[^\r\n]+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            catch { return null; }
        }

        private static string Sanitise(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return string.Concat(value.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ' || c == '.'));
        }

        private static string Norm(string? v) =>
            string.IsNullOrWhiteSpace(v) ? "" : v.Trim().ToUpperInvariant();

        private static bool NumericMatch(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b)) return true;
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            if (decimal.TryParse(a.Trim(), out var da) && decimal.TryParse(b.Trim(), out var db))
                return da == db;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHeqfIndicated(string? accreditationRef, IReadOnlyList<string> codes)
        {
            if (string.IsNullOrWhiteSpace(accreditationRef)) return false;
            var upper = accreditationRef.Trim().ToUpperInvariant();
            return codes.Any(code => upper.Contains(code.Trim().ToUpperInvariant()));
        }

        private static List<string> ParseHeqfCodes(string csv) =>
            (csv ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(c => c.Length > 0)
                .ToList();

        // ── Per-qualification validation ─────────────────────────────────────

        private static Rule38ValidationRow ValidateQualification(
            int rowNo,
            string qualCode, string qualName, string approvalStatus,
            string? qualType, string? minTimeTotal, string? minTimeWIL, string? heqfIndicator, string? totalSubsidy,
            bool hasPqmMatch,
            string? pqmQualType, string? pqmMinTimeTotal, string? pqmWIL, string? pqmAccreditation, string? pqmTotalSubsidy,
            IReadOnlyList<string> heqfCodes)
        {
            var failed = new List<string>();

            if (!hasPqmMatch)
            {
                return new Rule38ValidationRow
                {
                    ValidationNumber = rowNo,
                    QualCode = qualCode,
                    QualName = qualName,
                    ApprovalStatus = approvalStatus,
                    QualType = qualType,
                    MinTimeTotal = minTimeTotal,
                    MinTimeWIL = minTimeWIL,
                    HeqfIndicator = heqfIndicator,
                    TotalSubsidy = totalSubsidy,
                    HasPqmMatch = false,
                    ValidationResult = "FAIL",
                    FailedControls = new List<string> { "No PQM match", "C2", "C3", "C4", "C5", "C6" }
                };
            }

            // C2 (5.1.2): Qualification type matches PQM
            var c2 = string.Equals(Norm(qualType), Norm(pqmQualType), StringComparison.OrdinalIgnoreCase);
            if (!c2) failed.Add("C2");

            // C3 (5.1.3): Minimum time total matches PQM Total2
            var c3 = NumericMatch(minTimeTotal, pqmMinTimeTotal);
            if (!c3) failed.Add("C3");

            // C4 (5.1.4): Minimum time WIL matches PQM WIL_EL2
            var c4 = NumericMatch(minTimeWIL, pqmWIL);
            if (!c4) failed.Add("C4");

            // C5 (5.1.5): HEQF/HEQSF indicator matches expected based on accreditation ref
            var expectedHeqf = IsHeqfIndicated(pqmAccreditation, heqfCodes) ? "Y" : "N";
            var c5 = string.Equals(Norm(heqfIndicator), expectedHeqf, StringComparison.OrdinalIgnoreCase);
            if (!c5) failed.Add("C5");

            // C6 (5.1.6): Total subsidy units matches PQM Total2
            var c6 = NumericMatch(totalSubsidy, pqmTotalSubsidy);
            if (!c6) failed.Add("C6");

            return new Rule38ValidationRow
            {
                ValidationNumber = rowNo,
                QualCode = qualCode,
                QualName = qualName,
                ApprovalStatus = approvalStatus,
                QualType = qualType,
                MinTimeTotal = minTimeTotal,
                MinTimeWIL = minTimeWIL,
                HeqfIndicator = heqfIndicator,
                TotalSubsidy = totalSubsidy,
                HasPqmMatch = true,
                PqmQualType = pqmQualType,
                PqmMinTimeTotal = pqmMinTimeTotal,
                PqmWIL = pqmWIL,
                PqmAccreditation = pqmAccreditation,
                PqmTotalSubsidy = pqmTotalSubsidy,
                C2_TypeMatch = c2,
                C3_MinTimeMatch = c3,
                C4_WILMatch = c4,
                C5_HeqfMatch = c5,
                C5_ExpectedHeqf = expectedHeqf,
                C6_SubsidyMatch = c6,
                ValidationResult = failed.Count == 0 ? "PASS" : "FAIL",
                FailedControls = failed
            };
        }

        private static List<Rule38ControlSummary> BuildControlSummaries(
            List<Rule38ValidationRow> rows,
            string qualTable, string qualApprovalCol, string qualApprovalValue,
            string qualTypeCol, string pqmQualTypeCol,
            string qualMinTimeTotalCol, string pqmMinTimeTotalCol,
            string qualMinTimeWilCol, string pqmWilCol,
            string qualHeqfCol, string pqmAccreditationCol,
            string qualTotalSubsidyCol, string pqmTotalSubsidyCol)
        {
            var matched = rows.Where(r => r.HasPqmMatch).ToList();
            return new List<Rule38ControlSummary>
            {
                new() {
                    ControlId    = "C2",
                    ControlLabel = "Control 2 (5.1.2) — Qualification Type",
                    CriteriaText = $"{qualTable}.{qualTypeCol} = PQM.{pqmQualTypeCol}",
                    PassCount    = matched.Count(r => r.C2_TypeMatch),
                    FailCount    = matched.Count(r => !r.C2_TypeMatch) + rows.Count(r => !r.HasPqmMatch),
                    Status       = matched.All(r => r.C2_TypeMatch) && rows.All(r => r.HasPqmMatch) ? "PASS" : "FAIL"
                },
                new() {
                    ControlId    = "C3",
                    ControlLabel = "Control 3 (5.1.3) — Minimum Time: Total",
                    CriteriaText = $"{qualTable}.{qualMinTimeTotalCol} = PQM.{pqmMinTimeTotalCol}",
                    PassCount    = matched.Count(r => r.C3_MinTimeMatch),
                    FailCount    = matched.Count(r => !r.C3_MinTimeMatch) + rows.Count(r => !r.HasPqmMatch),
                    Status       = matched.All(r => r.C3_MinTimeMatch) && rows.All(r => r.HasPqmMatch) ? "PASS" : "FAIL"
                },
                new() {
                    ControlId    = "C4",
                    ControlLabel = "Control 4 (5.1.4) — Minimum Time: WIL/Experiential",
                    CriteriaText = $"{qualTable}.{qualMinTimeWilCol} = PQM.{pqmWilCol}",
                    PassCount    = matched.Count(r => r.C4_WILMatch),
                    FailCount    = matched.Count(r => !r.C4_WILMatch) + rows.Count(r => !r.HasPqmMatch),
                    Status       = matched.All(r => r.C4_WILMatch) && rows.All(r => r.HasPqmMatch) ? "PASS" : "FAIL"
                },
                new() {
                    ControlId    = "C5",
                    ControlLabel = "Control 5 (5.1.5) — HEQF/HEQSF Indicator",
                    CriteriaText = $"{qualTable}.{qualHeqfCol} (Y/N) agrees with PQM.{pqmAccreditationCol} indicator codes",
                    PassCount    = matched.Count(r => r.C5_HeqfMatch),
                    FailCount    = matched.Count(r => !r.C5_HeqfMatch) + rows.Count(r => !r.HasPqmMatch),
                    Status       = matched.All(r => r.C5_HeqfMatch) && rows.All(r => r.HasPqmMatch) ? "PASS" : "FAIL"
                },
                new() {
                    ControlId    = "C6",
                    ControlLabel = "Control 6 (5.1.6) — Total Subsidy Units",
                    CriteriaText = $"{qualTable}.{qualTotalSubsidyCol} = PQM.{pqmTotalSubsidyCol}",
                    PassCount    = matched.Count(r => r.C6_SubsidyMatch),
                    FailCount    = matched.Count(r => !r.C6_SubsidyMatch) + rows.Count(r => !r.HasPqmMatch),
                    Status       = matched.All(r => r.C6_SubsidyMatch) && rows.All(r => r.HasPqmMatch) ? "PASS" : "FAIL"
                }
            };
        }

        private static void ApplyBrowserPreview(Rule38ValidationSummary summary)
        {
            if (summary.ValidationRows.Count > BrowserPreviewRowLimit)
            {
                var failRows = summary.ValidationRows
                    .Where(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var passRows = summary.ValidationRows
                    .Where(r => string.Equals(r.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var failTake = Math.Min(failRows.Count, Math.Max(BrowserPreviewRowLimit / 2, 1));
                var passTake = Math.Min(passRows.Count, BrowserPreviewRowLimit - failTake);
                if (failTake == 0) passTake = Math.Min(passRows.Count, BrowserPreviewRowLimit);
                else if (passTake == 0) failTake = Math.Min(failRows.Count, BrowserPreviewRowLimit);

                summary.ValidationRows = failRows.Take(failTake)
                    .Concat(passRows.Take(passTake))
                    .Take(BrowserPreviewRowLimit)
                    .ToList();
                summary.IsPreviewOnly = true;
                summary.PreviewLimit = BrowserPreviewRowLimit;
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        public async Task<DatabaseListResult> GetDatabasesAsync(string server, string driver)
        {
            try
            {
                var connStr = BuildConnectionString(server, "master", driver);
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

        public async Task<Rule38TableListResult> GetTablesAsync(string server, string database, string driver)
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

                return new Rule38TableListResult
                {
                    Success = true,
                    Tables = tables,
                    AutoQualTable = tables.FirstOrDefault(t =>
                        t.Equals("dbo_QUAL", StringComparison.OrdinalIgnoreCase)),
                    AutoPqmTable = tables.FirstOrDefault(t =>
                        t.Equals("PQM", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("PQM", StringComparison.OrdinalIgnoreCase))
                };
            }
            catch (Exception ex)
            {
                return new Rule38TableListResult { Success = false, Error = ex.Message };
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

                string? auto = tableRole?.ToLowerInvariant() switch
                {
                    "qual_id"       => columns.FirstOrDefault(c => c.Equals("_001", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_name"     => columns.FirstOrDefault(c => c.Equals("_003", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_approval" => columns.FirstOrDefault(c => c.Equals("_004", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_type"     => columns.FirstOrDefault(c => c.Equals("_005", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_053"      => columns.FirstOrDefault(c => c.Equals("_053", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_054"      => columns.FirstOrDefault(c => c.Equals("_054", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_084"      => columns.FirstOrDefault(c => c.Equals("_084", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "qual_090"      => columns.FirstOrDefault(c => c.Equals("_090", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_name"      => columns.FirstOrDefault(c => c.Contains("Authorised", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_type"      => columns.FirstOrDefault(c => c.Contains("HEQF_Qual", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_total"     => columns.FirstOrDefault(c => c.Equals("Total2", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_wil"       => columns.FirstOrDefault(c => c.Equals("WIL_EL2", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_accred"    => columns.FirstOrDefault(c => c.Contains("CHE_HEQC", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    _               => columns.FirstOrDefault()
                };

                return new ColumnListResult { Success = true, Columns = columns, AutoSelected = auto };
            }
            catch (Exception ex)
            {
                return new ColumnListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule38VerifyResult> VerifyDataAsync(Rule38VerifyRequest request)
        {
            try
            {
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var qt = Sanitise(request.QualTable);
                var pt = Sanitise(request.PqmTable);
                var qa = Sanitise(request.QualApprovalCol);
                var av = (request.QualApprovalValue ?? "A").Replace("'", "''").Trim().ToUpperInvariant();

                var sql = $@"
SELECT
    (SELECT COUNT(*) FROM [{qt}]) AS QualTotal,
    (SELECT COUNT(*) FROM [{qt}] WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50), [{qa}])))) = '{av}') AS ApprovedCount,
    (SELECT COUNT(*) FROM [{pt}]) AS PqmTotal";

                using var cmd = new SqlCommand(sql, conn).WithLargeDataTimeout();
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new Rule38VerifyResult
                    {
                        Success = true,
                        QualTotal = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
                        ApprovedCount = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                        PqmTotal = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2))
                    };
                }
                return new Rule38VerifyResult { Success = false, Error = "No data returned." };
            }
            catch (Exception ex)
            {
                return new Rule38VerifyResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule38ValidationSummary> RunValidationAsync(
            Rule38ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var qt  = Sanitise(request.QualTable);
                var pt  = Sanitise(request.PqmTable);
                var qi  = Sanitise(request.QualIdCol);
                var qn  = Sanitise(request.QualNameCol);
                var qa  = Sanitise(request.QualApprovalCol);
                var qt5 = Sanitise(request.QualTypeCol);
                var q53 = Sanitise(request.QualMinTimeTotalCol);
                var q54 = Sanitise(request.QualMinTimeWilCol);
                var q84 = Sanitise(request.QualHeqfCol);
                var q90 = Sanitise(request.QualTotalSubsidyCol);
                var pn  = Sanitise(request.PqmNameCol);
                var pt5 = Sanitise(request.PqmQualTypeCol);
                var p53 = Sanitise(request.PqmMinTimeTotalCol);
                var p54 = Sanitise(request.PqmWilCol);
                var p84 = Sanitise(request.PqmAccreditationCol);
                var p90 = Sanitise(request.PqmTotalSubsidyCol);
                var av  = (request.QualApprovalValue ?? "A").Replace("'", "''").Trim().ToUpperInvariant();
                var mExclude      = request.ExcludeMPrefixPattern ? $" AND UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255),Q.[{qi}])))) NOT LIKE 'M_____'" : "";
                var mExcludeCount = request.ExcludeMPrefixPattern ? $" AND UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255),[{qi}])))) NOT LIKE 'M_____'" : "";

                // Count totals
                var qualTotal = 0;
                var approvedCount = 0;
                using (var cmd = new SqlCommand(
                    $"SELECT COUNT(*) FROM [{qt}];" +
                    $"SELECT COUNT(*) FROM [{qt}] WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50),[{qa}])))) = '{av}'{mExcludeCount};", conn)
                    .WithLargeDataTimeout())
                {
                    using var r = await cmd.ExecuteReaderAsync();
                    if (await r.ReadAsync()) qualTotal = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0));
                    if (await r.NextResultAsync() && await r.ReadAsync()) approvedCount = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0));
                }

                // Load QUAL approved records
                var sql = $@"
SELECT
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), Q.[{qi}])))) AS QualCode,
    LTRIM(RTRIM(CONVERT(nvarchar(500), Q.[{qn}]))) AS QualName,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50),  Q.[{qa}])))) AS ApprovalStatus,
    LTRIM(RTRIM(CONVERT(nvarchar(100), Q.[{qt5}]))) AS QualType,
    LTRIM(RTRIM(CONVERT(nvarchar(50),  Q.[{q53}]))) AS MinTimeTotal,
    LTRIM(RTRIM(CONVERT(nvarchar(50),  Q.[{q54}]))) AS MinTimeWIL,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(10),  Q.[{q84}])))) AS HeqfIndicator,
    LTRIM(RTRIM(CONVERT(nvarchar(50),  Q.[{q90}]))) AS TotalSubsidy,
    LTRIM(RTRIM(CONVERT(nvarchar(100), P.[{pt5}]))) AS PqmQualType,
    LTRIM(RTRIM(CONVERT(nvarchar(50),  P.[{p53}]))) AS PqmMinTimeTotal,
    LTRIM(RTRIM(CONVERT(nvarchar(50),  P.[{p54}]))) AS PqmWIL,
    LTRIM(RTRIM(CONVERT(nvarchar(500), P.[{p84}]))) AS PqmAccreditation,
    LTRIM(RTRIM(CONVERT(nvarchar(50),  P.[{p90}]))) AS PqmTotalSubsidy,
    CASE WHEN P.[{pn}] IS NULL THEN 0 ELSE 1 END AS HasPqmMatch
FROM [{qt}] Q
LEFT JOIN [{pt}] P
    ON UPPER(LTRIM(RTRIM(CONVERT(nvarchar(500), Q.[{qn}])))) = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(500), P.[{pn}]))))
WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50), Q.[{qa}])))) = '{av}'{mExclude}
ORDER BY Q.[{qi}]";

                var heqfCodes = ParseHeqfCodes(request.HeqfIndicatorCodesCsv);
                var rows = new List<Rule38ValidationRow>();
                int rowNo = 1;

                using (var cmd = new SqlCommand(sql, conn).WithLargeDataTimeout())
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string Read(int i) => reader.IsDBNull(i) ? "" : (reader.GetValue(i)?.ToString() ?? "");
                        string? ReadNullable(int i) => reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString();

                        var hasPqmMatch = !reader.IsDBNull(13) && Convert.ToInt32(reader.GetValue(13)) == 1;

                        rows.Add(ValidateQualification(
                            rowNo++,
                            Read(0), Read(1), Read(2),
                            ReadNullable(3), ReadNullable(4), ReadNullable(5), ReadNullable(6), ReadNullable(7),
                            hasPqmMatch,
                            hasPqmMatch ? ReadNullable(8)  : null,
                            hasPqmMatch ? ReadNullable(9)  : null,
                            hasPqmMatch ? ReadNullable(10) : null,
                            hasPqmMatch ? ReadNullable(11) : null,
                            hasPqmMatch ? ReadNullable(12) : null,
                            heqfCodes));
                    }
                }

                var pqmMatchCount   = rows.Count(r => r.HasPqmMatch);
                var pqmNoMatchCount = rows.Count(r => !r.HasPqmMatch);
                var overallPass     = rows.Count(r => r.ValidationResult == "PASS");
                var overallFail     = rows.Count(r => r.ValidationResult == "FAIL");
                var total           = rows.Count;
                var rate            = total > 0 ? Math.Round((decimal)overallFail / total * 100, 2) : 0m;

                var controlSummaries = BuildControlSummaries(rows,
                    request.QualTable, request.QualApprovalCol, request.QualApprovalValue,
                    request.QualTypeCol, request.PqmQualTypeCol,
                    request.QualMinTimeTotalCol, request.PqmMinTimeTotalCol,
                    request.QualMinTimeWilCol, request.PqmWilCol,
                    request.QualHeqfCol, request.PqmAccreditationCol,
                    request.QualTotalSubsidyCol, request.PqmTotalSubsidyCol);

                var summary = new Rule38ValidationSummary
                {
                    Success              = true,
                    TotalQualRecords     = qualTotal,
                    ApprovedCount        = approvedCount,
                    PqmMatchCount        = pqmMatchCount,
                    PqmNoMatchCount      = pqmNoMatchCount,
                    OverallPassCount     = overallPass,
                    OverallFailCount     = overallFail,
                    ExceptionRate        = rate,
                    Status               = overallFail == 0 ? "PASS" : "FAIL",
                    Timestamp            = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Database             = request.Database,
                    QualTable            = request.QualTable,
                    QualIdCol            = request.QualIdCol,
                    QualNameCol          = request.QualNameCol,
                    QualApprovalCol      = request.QualApprovalCol,
                    QualApprovalValue    = request.QualApprovalValue,
                    QualTypeCol          = request.QualTypeCol,
                    QualMinTimeTotalCol  = request.QualMinTimeTotalCol,
                    QualMinTimeWilCol    = request.QualMinTimeWilCol,
                    QualHeqfCol          = request.QualHeqfCol,
                    QualTotalSubsidyCol  = request.QualTotalSubsidyCol,
                    PqmTable             = request.PqmTable,
                    PqmNameCol           = request.PqmNameCol,
                    PqmQualTypeCol       = request.PqmQualTypeCol,
                    PqmMinTimeTotalCol   = request.PqmMinTimeTotalCol,
                    PqmWilCol            = request.PqmWilCol,
                    PqmAccreditationCol  = request.PqmAccreditationCol,
                    PqmTotalSubsidyCol   = request.PqmTotalSubsidyCol,
                    HeqfIndicatorCodesCsv = request.HeqfIndicatorCodesCsv,
                    ExcludeMPrefixPattern = request.ExcludeMPrefixPattern,
                    PostgraduateTypesCsv  = request.PostgraduateTypesCsv,
                    ClientId             = request.ClientId,
                    ControlSummaries     = controlSummaries,
                    ValidationRows       = rows
                };

                if (request.ClientId > 0)
                {
                    await using var systemConn = await OpenSystemConnectionAsync();
                    var systemUserId = await GetSystemUserIdByEmailAsync(systemConn, userEmail);
                    if (!systemUserId.HasValue)
                        throw new InvalidOperationException("Current user is not available in the system database.");

                    await EnsureClientNotArchivedAsync(systemConn, request.ClientId);
                    await ClearRuleSignoffsAsync(systemConn, request.ClientId, 38);
                    await MarkPreviousRunsHistoricalAsync(systemConn, request.ClientId, 38);

                    var runId = await InsertValidationRunAsync(systemConn, request, summary, systemUserId.Value, userName);
                    summary.SavedRunId = runId;

                    await using var update = systemConn.CreateConfiguredCommand();
                    update.CommandText = "UPDATE dbo.ValidationRuns SET ResultsJSON = @ResultsJSON WHERE RunID = @RunID;";
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
                return new Rule38ValidationSummary { Success = false, Error = ex.Message };
            }
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID AND RuleNumber = 38;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        public async Task<Rule38WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(
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
    ISNULL(vr.Status,        '') AS Status,
    ISNULL(vr.LastEditedByUserName, '') AS LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON,
    vr.WorkspaceSavedAt
FROM dbo.ValidationRuns vr
WHERE vr.ClientID   = @ClientID
  AND vr.RuleNumber = 38
  AND vr.IsCurrent  = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var runId       = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
            var server      = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var database    = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var status      = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var lastEditor  = reader.IsDBNull(5) ? null : reader.GetString(5);
            var lastEditedAt = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6);
            var resultsJson = reader.IsDBNull(7) ? null : reader.GetString(7);
            var savedAt     = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8);
            await reader.CloseAsync();

            Rule38ValidationSummary? deserializedSummary = null;
            if (!string.IsNullOrWhiteSpace(resultsJson))
            {
                try
                {
                    var decoded = ValidationPayloadCodec.Decode(resultsJson);
                    deserializedSummary = JsonConvert.DeserializeObject<Rule38ValidationSummary>(decoded);
                }
                catch { }
            }

            var signoffs = await LoadSignoffsAsync(connection, runId, 38);
            var daSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            string? currentUserSignoffComment = null;
            bool currentUserHasSigned = false;
            string currentUserRole = "";

            if (!string.IsNullOrWhiteSpace(currentUserEmail))
            {
                var userSignoff = signoffs.FirstOrDefault(s =>
                    string.Equals(s.ReviewerEmail, currentUserEmail, StringComparison.OrdinalIgnoreCase));
                if (userSignoff != null)
                {
                    currentUserHasSigned = true;
                    currentUserSignoffComment = userSignoff.Comment;
                }
                currentUserRole = await GetEngagementRoleAsync(connection, clientId, currentUserEmail);
            }

            return new Rule38WorkspaceStateViewModel
            {
                ClientId               = clientId,
                RunId                  = runId > 0 ? runId : null,
                Server                 = server,
                Database               = database,
                Driver                 = deserializedSummary != null ? "ODBC Driver 17 for SQL Server" : "ODBC Driver 17 for SQL Server",
                QualTable              = deserializedSummary?.QualTable ?? "dbo_QUAL",
                QualIdCol              = deserializedSummary?.QualIdCol ?? "_001",
                QualNameCol            = deserializedSummary?.QualNameCol ?? "_003",
                QualApprovalCol        = deserializedSummary?.QualApprovalCol ?? "_004",
                QualApprovalValue      = deserializedSummary?.QualApprovalValue ?? "A",
                QualTypeCol            = deserializedSummary?.QualTypeCol ?? "_005",
                QualMinTimeTotalCol    = deserializedSummary?.QualMinTimeTotalCol ?? "_053",
                QualMinTimeWilCol      = deserializedSummary?.QualMinTimeWilCol ?? "_054",
                QualHeqfCol            = deserializedSummary?.QualHeqfCol ?? "_084",
                QualTotalSubsidyCol    = deserializedSummary?.QualTotalSubsidyCol ?? "_090",
                PqmTable               = deserializedSummary?.PqmTable ?? "PQM",
                PqmNameCol             = deserializedSummary?.PqmNameCol ?? "Authorised_Qualification_Name",
                PqmQualTypeCol         = deserializedSummary?.PqmQualTypeCol ?? "HEQF_Qual_Type",
                PqmMinTimeTotalCol     = deserializedSummary?.PqmMinTimeTotalCol ?? "Total2",
                PqmWilCol              = deserializedSummary?.PqmWilCol ?? "WIL_EL2",
                PqmAccreditationCol    = deserializedSummary?.PqmAccreditationCol ?? "CHE_HEQC_Accreditation_Approval_Ref_Nr",
                PqmTotalSubsidyCol     = deserializedSummary?.PqmTotalSubsidyCol ?? "Total2",
                HeqfIndicatorCodesCsv  = deserializedSummary?.HeqfIndicatorCodesCsv ?? "H/,HEQF,HEQSF",
                ExcludeMPrefixPattern  = deserializedSummary?.ExcludeMPrefixPattern ?? false,
                PostgraduateTypesCsv   = deserializedSummary?.PostgraduateTypesCsv ?? "07,27,28,49,72,73,08,30,50,74,75",
                CurrentUserEngagementRole = currentUserRole,
                HasDataAnalystSignoff  = daSignoff,
                CurrentUserHasSignedOff = currentUserHasSigned,
                CurrentUserSignoffComment = currentUserSignoffComment ?? "",
                CurrentStatus          = status,
                LastEditedByUserName   = lastEditor,
                LastEditedAt           = lastEditedAt,
                IsWorkspaceSaved       = savedAt.HasValue,
                Summary                = deserializedSummary
            };
        }

        public async Task<Rule38RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID, vr.ClientID, vr.IsCurrent,
    e.EngagementName, e.MaconomyNumber,
    ISNULL(vr.HemisServer, '') AS HemisServer,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
LEFT JOIN dbo.Engagements e ON e.EngagementID = vr.ClientID
WHERE vr.RunID = @RunID AND vr.RuleNumber = 38;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var clientId        = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
            var isCurrent       = !reader.IsDBNull(2) && Convert.ToBoolean(reader.GetValue(2));
            var engagementName  = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var maconomyNumber  = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var sourceServer    = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var resultsJson     = reader.IsDBNull(6) ? null : reader.GetString(6);
            await reader.CloseAsync();

            Rule38ValidationSummary summary = new();
            if (!string.IsNullOrWhiteSpace(resultsJson))
            {
                try
                {
                    var decoded = ValidationPayloadCodec.Decode(resultsJson);
                    summary = JsonConvert.DeserializeObject<Rule38ValidationSummary>(decoded) ?? summary;
                }
                catch { }
            }

            var signoffs = await LoadSignoffsAsync(connection, runId, 38);
            var daSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            var currentUserRole = "";
            if (!string.IsNullOrWhiteSpace(currentUserEmail))
                currentUserRole = await GetEngagementRoleAsync(connection, clientId, currentUserEmail);

            return new Rule38RunReviewViewModel
            {
                RunId                   = runId,
                ClientId                = clientId,
                IsCurrentRun            = isCurrent,
                EngagementName          = engagementName,
                MaconomyNumber          = maconomyNumber,
                SourceServer            = sourceServer,
                GeneratedSql            = GenerateSql(new Rule38ValidationRequest
                {
                    Server              = sourceServer,
                    Database            = summary.Database,
                    QualTable           = summary.QualTable,
                    QualIdCol           = summary.QualIdCol,
                    QualNameCol         = summary.QualNameCol,
                    QualApprovalCol     = summary.QualApprovalCol,
                    QualApprovalValue   = summary.QualApprovalValue,
                    QualTypeCol         = summary.QualTypeCol,
                    QualMinTimeTotalCol = summary.QualMinTimeTotalCol,
                    QualMinTimeWilCol   = summary.QualMinTimeWilCol,
                    QualHeqfCol         = summary.QualHeqfCol,
                    QualTotalSubsidyCol = summary.QualTotalSubsidyCol,
                    PqmTable            = summary.PqmTable,
                    PqmNameCol          = summary.PqmNameCol,
                    PqmQualTypeCol      = summary.PqmQualTypeCol,
                    PqmMinTimeTotalCol  = summary.PqmMinTimeTotalCol,
                    PqmWilCol           = summary.PqmWilCol,
                    PqmAccreditationCol = summary.PqmAccreditationCol,
                    PqmTotalSubsidyCol  = summary.PqmTotalSubsidyCol,
                    HeqfIndicatorCodesCsv = summary.HeqfIndicatorCodesCsv,
                    ExcludeMPrefixPattern = summary.ExcludeMPrefixPattern,
                    PostgraduateTypesCsv  = summary.PostgraduateTypesCsv
                }),
                Summary                 = summary,
                Signoffs                = signoffs,
                CurrentUserEngagementRole = currentUserRole,
                HasDataAnalystSignoff   = daSignoff
            };
        }

        public async Task<Rule38WorkspaceSaveResult> SaveWorkspaceAsync(
            Rule38ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                    return new Rule38WorkspaceSaveResult { Success = false, Error = "Run validation before saving the workspace." };

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                    return new Rule38WorkspaceSaveResult { Success = false, Error = "The saved workspace could not be found for this engagement." };

                await EnsureClientNotArchivedAsync(connection, request.ClientId);

                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                    return new Rule38WorkspaceSaveResult { Success = false, Error = "Current user is not available in the system database." };

                var cleared      = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                var previousHash = await GetValidationRecordHashAsync(connection, request.RunId.Value);

                await using var cmd = connection.CreateConfiguredCommand();
                cmd.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer          = @HemisServer,
    AuditDatabase        = @AuditDatabase,
    StudTable            = @QualTable,
    DeceasedTable        = @PqmTable,
    StudColumn           = @HeqfCodes,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    WorkspaceSavedAt     = GETDATE(),
    PreviousHash         = @PreviousHash,
    RecordHash           = @RecordHash,
    Status               = 'Needs Review'
WHERE RunID = @RunID AND ClientID = @ClientID;";
                cmd.Parameters.AddWithValue("@RunID",               request.RunId.Value);
                cmd.Parameters.AddWithValue("@ClientID",            request.ClientId);
                cmd.Parameters.AddWithValue("@HemisServer",         request.Server);
                cmd.Parameters.AddWithValue("@AuditDatabase",       request.Database);
                cmd.Parameters.AddWithValue("@QualTable",           request.QualTable);
                cmd.Parameters.AddWithValue("@PqmTable",            request.PqmTable);
                cmd.Parameters.AddWithValue("@HeqfCodes",           request.HeqfIndicatorCodesCsv);
                cmd.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PreviousHash",        (object?)previousHash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RecordHash", ComputeHash(
                    $"WorkspaceSave|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|" +
                    $"{request.QualTable}|{request.PqmTable}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await cmd.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail);
                return new Rule38WorkspaceSaveResult
                {
                    Success             = true,
                    Message             = cleared > 0
                        ? "Workspace saved. Existing signoffs were removed and the run must be reviewed again."
                        : "Workspace saved and marked for review again.",
                    SignoffsCleared     = cleared > 0,
                    ClearedSignoffCount = cleared,
                    Workspace           = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule38WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule38WorkspaceSaveResult> BeginWorkspaceEditAsync(
            int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                    return new Rule38WorkspaceSaveResult { Success = false, Error = "Current user is not available in the system database." };

                var clientId = await GetClientIdForRunAsync(runId);
                if (!clientId.HasValue)
                    return new Rule38WorkspaceSaveResult { Success = false, Error = "Saved workspace was not found." };

                await EnsureClientNotArchivedAsync(connection, clientId.Value);

                var cleared      = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash = await GetValidationRecordHashAsync(connection, runId);

                await using var markEdit = connection.CreateConfiguredCommand();
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

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail);
                return new Rule38WorkspaceSaveResult
                {
                    Success             = true,
                    Message             = cleared > 0
                        ? "Editing has begun. Existing signoffs were removed so the workspace must be reviewed again."
                        : "Editing has begun.",
                    SignoffsCleared     = cleared > 0,
                    ClearedSignoffCount = cleared,
                    Workspace           = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule38WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var userId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!userId.HasValue) throw new InvalidOperationException("User not found.");

            var clientId = await GetClientIdForRunAsync(runId);
            if (clientId.HasValue) await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var role = clientId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId.Value, reviewerEmail)
                : "DataAnalyst";

            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
MERGE dbo.ValidationRunSignoffs AS target
USING (SELECT @RunID AS RunID, @UserID AS UserID) AS source ON target.RunID = source.RunID AND target.UserID = source.UserID
WHEN MATCHED THEN UPDATE SET Comment = @Comment, SignedOffAt = GETDATE(), SignoffRole = @SignoffRole
WHEN NOT MATCHED THEN INSERT (RunID, UserID, Comment, SignedOffAt, SignoffRole) VALUES (@RunID, @UserID, @Comment, GETDATE(), @SignoffRole);";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@UserID", userId.Value);
            cmd.Parameters.AddWithValue("@Comment", (object?)comment ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SignoffRole", role);
            await cmd.ExecuteNonQueryAsync();

            await UpdateRunStatusAfterSignoffAsync(connection, runId, 38);
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var userId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!userId.HasValue) return;

            var clientId = await GetClientIdForRunAsync(runId);
            if (clientId.HasValue) await EnsureClientNotArchivedAsync(connection, clientId.Value);

            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "DELETE FROM dbo.ValidationRunSignoffs WHERE RunID = @RunID AND UserID = @UserID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@UserID", userId.Value);
            await cmd.ExecuteNonQueryAsync();

            await UpdateRunStatusAfterSignoffAsync(connection, runId, 38);
        }

        public string GenerateSql(Rule38ValidationRequest request)
        {
            var qt  = Sanitise(request.QualTable);
            var pt  = Sanitise(request.PqmTable);
            var qi  = Sanitise(request.QualIdCol);
            var qn  = Sanitise(request.QualNameCol);
            var qa  = Sanitise(request.QualApprovalCol);
            var qt5 = Sanitise(request.QualTypeCol);
            var q53 = Sanitise(request.QualMinTimeTotalCol);
            var q54 = Sanitise(request.QualMinTimeWilCol);
            var q84 = Sanitise(request.QualHeqfCol);
            var q90 = Sanitise(request.QualTotalSubsidyCol);
            var pn  = Sanitise(request.PqmNameCol);
            var pt5 = Sanitise(request.PqmQualTypeCol);
            var p53 = Sanitise(request.PqmMinTimeTotalCol);
            var p54 = Sanitise(request.PqmWilCol);
            var p84 = Sanitise(request.PqmAccreditationCol);
            var p90 = Sanitise(request.PqmTotalSubsidyCol);
            var av  = (request.QualApprovalValue ?? "A").Replace("'", "''").Trim().ToUpperInvariant();
            var mExcludeSql = request.ExcludeMPrefixPattern ? $"\nAND UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255),Q.[{qi}])))) NOT LIKE 'M_____'" : "";

            return $@"-- HEMIS Rule 38: Enhanced QUAL vs PQM Validation
-- 5.1.1  Approved qualifications: [{qa}] = '{av}'{(request.ExcludeMPrefixPattern ? " (M_____ codes excluded)" : "")}
-- 5.1.2  Qualification type: [{qt5}] vs PQM.[{pt5}]
-- 5.1.3  Minimum Time Total: [{q53}] vs PQM.[{p53}]
-- 5.1.4  Minimum Time WIL: [{q54}] vs PQM.[{p54}]
-- 5.1.5  HEQF/HEQSF Indicator: [{q84}] (Y/N) vs PQM.[{p84}] using codes: {request.HeqfIndicatorCodesCsv}
-- 5.1.6  Total Subsidy Units: [{q90}] vs PQM.[{p90}]

SELECT
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), Q.[{qi}])))) AS QualCode,
    LTRIM(RTRIM(CONVERT(nvarchar(500), Q.[{qn}]))) AS QualName,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50),  Q.[{qa}])))) AS ApprovalStatus,
    Q.[{qt5}] AS [{qt5}_QUAL],
    P.[{pt5}] AS [{pt5}_PQM],
    CASE WHEN UPPER(LTRIM(RTRIM(CONVERT(nvarchar(100),Q.[{qt5}])))) = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(100),P.[{pt5}])))) THEN 'PASS' ELSE 'FAIL' END AS C2_TypeMatch,
    Q.[{q53}] AS [{q53}_QUAL],
    P.[{p53}] AS [{p53}_PQM],
    CASE WHEN CONVERT(decimal(18,4), NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(50),Q.[{q53}]))),N'')) = CONVERT(decimal(18,4), NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(50),P.[{p53}]))),N'')) THEN 'PASS' ELSE 'FAIL' END AS C3_MinTimeMatch,
    Q.[{q54}] AS [{q54}_QUAL],
    P.[{p54}] AS [{p54}_PQM],
    CASE WHEN CONVERT(decimal(18,4), NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(50),Q.[{q54}]))),N'')) = CONVERT(decimal(18,4), NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(50),P.[{p54}]))),N'')) THEN 'PASS' ELSE 'FAIL' END AS C4_WILMatch,
    Q.[{q84}] AS [{q84}_QUAL],
    P.[{p84}] AS AccreditationRef,
    Q.[{q90}] AS [{q90}_QUAL],
    P.[{p90}] AS [{p90}_PQM],
    CASE WHEN CONVERT(decimal(18,4), NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(50),Q.[{q90}]))),N'')) = CONVERT(decimal(18,4), NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(50),P.[{p90}]))),N'')) THEN 'PASS' ELSE 'FAIL' END AS C6_SubsidyMatch
FROM [{qt}] Q
LEFT JOIN [{pt}] P
    ON UPPER(LTRIM(RTRIM(CONVERT(nvarchar(500), Q.[{qn}])))) = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(500), P.[{pn}]))))
WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50), Q.[{qa}])))) = '{av}'{mExcludeSql}
ORDER BY Q.[{qi}];".Trim();
        }

        // ── System DB helpers ─────────────────────────────────────────────────

        private string SystemConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("System connection string not configured.");

        private async Task<SqlConnection> OpenSystemConnectionAsync()
        {
            var conn = new SqlConnection(SystemConnectionString);
            await conn.OpenAsync();
            return conn;
        }

        private static async Task<int?> GetSystemUserIdByEmailAsync(SqlConnection conn, string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 Id FROM dbo.AspNetUsers WHERE Email = @Email;";
            cmd.Parameters.AddWithValue("@Email", email);
            var val = await cmd.ExecuteScalarAsync();
            return val == null || val == DBNull.Value ? null : Convert.ToInt32(val);
        }

        private static async Task EnsureClientNotArchivedAsync(SqlConnection conn, int clientId)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 IsArchived FROM dbo.Engagements WHERE EngagementID = @ID;";
            cmd.Parameters.AddWithValue("@ID", clientId);
            var val = await cmd.ExecuteScalarAsync();
            if (val != null && val != DBNull.Value && Convert.ToBoolean(val))
                throw new InvalidOperationException("This engagement is archived and cannot be modified.");
        }

        private static async Task ClearRuleSignoffsAsync(SqlConnection conn, int clientId, int ruleNumber)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
DELETE s FROM dbo.ValidationRunSignoffs s
INNER JOIN dbo.ValidationRuns r ON r.RunID = s.RunID
WHERE r.ClientID = @ClientID AND r.RuleNumber = @RuleNumber;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            cmd.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task MarkPreviousRunsHistoricalAsync(SqlConnection conn, int clientId, int ruleNumber)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
UPDATE dbo.ValidationRuns
SET IsCurrent = 0
WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber AND IsCurrent = 1;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            cmd.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<int> InsertValidationRunAsync(
            SqlConnection conn, Rule38ValidationRequest request,
            Rule38ValidationSummary summary, int userId, string? userName)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
INSERT INTO dbo.ValidationRuns
    (ClientID, UserID, RuleNumber, RuleName, RunByUserName,
     HemisServer, AuditDatabase, StudTable, DeceasedTable, StudColumn,
     TotalRecords, PassCount, FailCount, ExceptionRate,
     Status, IsCurrent, ExceptionsJSON, ResultsJSON, RecordHash)
OUTPUT INSERTED.RunID
VALUES
    (@ClientID, @UserID, 38, 'Enhanced QUAL vs PQM Validation', @UserName,
     @HemisServer, @AuditDatabase, @QualTable, @PqmTable, @HeqfCodes,
     @TotalCount, @PassCount, @FailCount, @ExceptionRate,
     @Status, 1, '[]', '{}', @RecordHash);";
            cmd.Parameters.AddWithValue("@ClientID",      request.ClientId);
            cmd.Parameters.AddWithValue("@UserID",        userId);
            cmd.Parameters.AddWithValue("@UserName",      (object?)userName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@HemisServer",   request.Server);
            cmd.Parameters.AddWithValue("@AuditDatabase", request.Database);
            cmd.Parameters.AddWithValue("@QualTable",     request.QualTable);
            cmd.Parameters.AddWithValue("@PqmTable",      request.PqmTable);
            cmd.Parameters.AddWithValue("@HeqfCodes",     request.HeqfIndicatorCodesCsv);
            cmd.Parameters.AddWithValue("@TotalCount",    summary.ApprovedCount);
            cmd.Parameters.AddWithValue("@PassCount",     summary.OverallPassCount);
            cmd.Parameters.AddWithValue("@FailCount",     summary.OverallFailCount);
            cmd.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            cmd.Parameters.AddWithValue("@Status",        summary.Status);
            cmd.Parameters.AddWithValue("@RecordHash",    ComputeHash(
                $"Rule38|{request.ClientId}|{request.Server}|{request.Database}|{request.QualTable}|{request.PqmTable}|{DateTime.UtcNow:o}"));

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        private static async Task<int> ClearSignoffsAndFlagForReviewAsync(SqlConnection conn, int runId)
        {
            await using var del = conn.CreateConfiguredCommand();
            del.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            del.Parameters.AddWithValue("@RunID", runId);
            return await del.ExecuteNonQueryAsync();
        }

        private static async Task<string?> GetValidationRecordHashAsync(SqlConnection conn, int runId)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            var val = await cmd.ExecuteScalarAsync();
            return val == null || val == DBNull.Value ? null : val.ToString();
        }

        private static async Task<string?> GetLatestValidationHashAsync(SqlConnection conn, int clientId, int ruleNumber)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber ORDER BY RunTimestamp DESC, RunID DESC;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            cmd.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var val = await cmd.ExecuteScalarAsync();
            return val == null || val == DBNull.Value ? null : val.ToString();
        }

        private static async Task<List<RunSignoffViewModel>> LoadSignoffsAsync(SqlConnection conn, int runId, int ruleNumber)
        {
            var signoffs = new List<RunSignoffViewModel>();
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT rs.SignoffID,
       ISNULL(rs.SignoffRole, '') AS SignoffRole,
       LTRIM(RTRIM(ISNULL(u.FirstName, '') + ' ' + ISNULL(u.LastName, ''))) AS ReviewerName,
       ISNULL(u.Email, '') AS ReviewerEmail,
       ISNULL(rs.Comment, '') AS Comment,
       rs.SignedOffAt
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.Users u ON u.UserID = rs.ReviewerID
WHERE rs.RunID = @RunID
ORDER BY CASE ISNULL(rs.SignoffRole,'')
           WHEN 'DataAnalyst' THEN 1 WHEN 'Manager' THEN 2 WHEN 'Director' THEN 3 ELSE 4 END,
         rs.SignedOffAt DESC;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                signoffs.Add(new RunSignoffViewModel
                {
                    Id            = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0)),
                    SignoffRole   = r.IsDBNull(1) ? "" : r.GetString(1),
                    ReviewerName  = r.IsDBNull(2) ? "" : r.GetString(2),
                    ReviewerEmail = r.IsDBNull(3) ? "" : r.GetString(3),
                    Comment       = r.IsDBNull(4) ? "" : r.GetString(4),
                    SignedOffAt   = r.IsDBNull(5) ? DateTime.UtcNow : r.GetDateTime(5)
                });
            }
            return signoffs;
        }

        private static async Task<string> GetEngagementRoleAsync(SqlConnection conn, int clientId, string email)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT TOP 1 ea.EngagementRole
FROM dbo.EngagementAssignments ea
INNER JOIN dbo.AspNetUsers u ON u.Id = ea.UserID
WHERE ea.EngagementID = @ClientID AND u.Email = @Email;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            cmd.Parameters.AddWithValue("@Email", email);
            var val = await cmd.ExecuteScalarAsync();
            return val == null || val == DBNull.Value ? "" : val.ToString() ?? "";
        }

        private static async Task UpdateRunStatusAfterSignoffAsync(SqlConnection conn, int runId, int ruleNumber)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
UPDATE dbo.ValidationRuns
SET Status = CASE
    WHEN EXISTS(SELECT 1 FROM dbo.ValidationRunSignoffs s
                INNER JOIN dbo.AspNetUsers u ON u.Id = s.UserID
                WHERE s.RunID = @RunID AND s.SignoffRole = 'Manager')
    THEN 'Manager Signed Off'
    WHEN EXISTS(SELECT 1 FROM dbo.ValidationRunSignoffs s WHERE s.RunID = @RunID AND s.SignoffRole = 'DataAnalyst')
    THEN 'Data Analyst Signed Off'
    ELSE 'Needs Review'
END
WHERE RunID = @RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            await cmd.ExecuteNonQueryAsync();
        }

        private static string ComputeHash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
