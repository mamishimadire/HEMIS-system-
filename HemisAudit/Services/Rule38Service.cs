using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule38Service : IRule38Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private static readonly string[] DefaultPostgraduateTypeCodes = ["07", "27", "28", "49", "72", "73", "08", "30", "50", "74", "75"];
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule38Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
        {
            _configuration = configuration;
            _pendingValidationCache = pendingValidationCache;
        }

        private static string BuildConnectionString(string server, string database, string driver)
        {
            server = (server ?? "").Trim();
            database = (database ?? "").Trim();

            if (server.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase))
            {
                var pipe = ResolveLocalDbPipe(server);
                if (!string.IsNullOrWhiteSpace(pipe))
                    return $"Server={pipe};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=30;";
            }

            return $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";
        }

        private static string? ResolveLocalDbPipe(string server)
        {
            try
            {
                var instance = server.Contains('\\') ? server.Split('\\').Last().Trim() : "MSSQLLocalDB";
                using (var startProcess = Process.Start(new ProcessStartInfo("sqllocaldb", $"start \"{instance}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                })!)
                {
                    startProcess.WaitForExit(8000);
                }

                using var infoProcess = Process.Start(new ProcessStartInfo("sqllocaldb", $"info \"{instance}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                })!;

                var output = infoProcess.StandardOutput.ReadToEnd();
                infoProcess.WaitForExit(3000);
                var match = Regex.Match(output, @"Instance pipe name:\s*(np:[^\r\n]+)", RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private static string Sanitise(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return string.Concat(value.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ' || c == '.'));
        }

        private static string Norm(string? v) =>
            string.IsNullOrWhiteSpace(v) ? "" : v.Trim().ToUpperInvariant();

        private static string NormName(string? v)
        {
            if (v == null) return "";
            return Regex.Replace(v.Trim().ToUpperInvariant(), @"\s+", " ");
        }

        private static string DigitsOnly(string? v) =>
            Regex.Replace(Norm(v), @"\D", "");

        private static string TrimLeadingZeros(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var trimmed = value.TrimStart('0');
            return trimmed.Length == 0 ? "0" : trimmed;
        }

        private static bool HasSameLeadingDigits(string left, string right, int digits) =>
            left.Length >= digits &&
            right.Length >= digits &&
            string.Equals(left[..digits], right[..digits], StringComparison.Ordinal);

        private static string? GetCesmReviewMatchReason(string? qualCode, string? pqmCode)
        {
            var rawQualCode = DigitsOnly(qualCode);
            var rawPqmCode = DigitsOnly(pqmCode);
            if (rawQualCode.Length == 0 || rawPqmCode.Length == 0) return null;

            if (HasSameLeadingDigits(rawQualCode, rawPqmCode, 4))
                return "first 4 digits matched";

            var trimmedQualCode = TrimLeadingZeros(rawQualCode);
            var trimmedPqmCode = TrimLeadingZeros(rawPqmCode);

            if (HasSameLeadingDigits(trimmedQualCode, trimmedPqmCode, 4))
                return "first 4 digits matched after removing leading zeros";

            if (HasSameLeadingDigits(rawQualCode, rawPqmCode, 3))
                return "first 3 digits matched";

            if (HasSameLeadingDigits(trimmedQualCode, trimmedPqmCode, 3))
                return "first 3 digits matched after removing leading zeros";

            return null;
        }

        private static string? GetExactCesmMatchColumn(string? qualCesmCode, PqmRow pqmRow)
        {
            if (string.Equals(Norm(pqmRow.CesmCode1), Norm(qualCesmCode), StringComparison.Ordinal))
                return "PQM.CESM_CODE1";

            if (string.Equals(Norm(pqmRow.CesmCode), Norm(qualCesmCode), StringComparison.Ordinal))
                return "PQM.CESM_CODE";

            return null;
        }

        private static (string? Reason, string? MatchColumn) GetCesmReviewMatch(string? qualCesmCode, PqmRow pqmRow)
        {
            var code1Reason = GetCesmReviewMatchReason(qualCesmCode, pqmRow.CesmCode1);
            if (code1Reason != null)
                return (code1Reason, "PQM.CESM_CODE1");

            var codeReason = GetCesmReviewMatchReason(qualCesmCode, pqmRow.CesmCode);
            if (codeReason != null)
                return (codeReason, "PQM.CESM_CODE");

            return (null, null);
        }

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

        private static HashSet<string> ParsePostgraduateTypeCodes(string? csv)
        {
            var codes = (csv ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Norm)
                .Where(code => code.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

            if (codes.Count == 0)
            {
                foreach (var code in DefaultPostgraduateTypeCodes)
                    codes.Add(code);
            }

            return codes;
        }

        private static bool ResolveUseMPrefixPopulationSplit(bool useMPrefixPopulationSplit, bool legacyExcludeMPrefixPattern) =>
            useMPrefixPopulationSplit || legacyExcludeMPrefixPattern;

        private static bool IsMPrefixQualificationCode(string? qualCode) =>
            Regex.IsMatch(Norm(qualCode), @"^M.{5}$", RegexOptions.CultureInvariant);

        private static (string PopulationType, string PopulationClassificationNote) ClassifyPopulationType(
            string qualCode,
            string? qualType,
            ISet<string> postgraduateTypeCodes,
            bool useMPrefixPopulationSplit)
        {
            var notes = new List<string>();
            if (useMPrefixPopulationSplit && IsMPrefixQualificationCode(qualCode))
                notes.Add("qualification code matched M_____");

            if (postgraduateTypeCodes.Contains(Norm(qualType)))
                notes.Add($"QUAL type {qualType} is in the configured postgraduate _005 list");

            if (notes.Count > 0)
                return ("Postgraduate", string.Join("; ", notes));

            return ("Undergraduate", "qualification did not match the configured postgraduate rules");
        }

        private static (string PopulationType, string PopulationClassificationNote) ClassifyPopulationType(
            QualRecord qual,
            ISet<string> postgraduateTypeCodes,
            bool useMPrefixPopulationSplit) =>
            ClassifyPopulationType(qual.QualCode, qual.QualType, postgraduateTypeCodes, useMPrefixPopulationSplit);

        // ── Per-qualification validation ─────────────────────────────────────

        private record QualRecord(
            string QualCode,
            string QualName,
            string ApprovalStatus,
            string? QualType,
            string? MinTimeTotal,
            string? MinTimeWIL,
            string? HeqfIndicator,
            string? TotalSubsidy,
            string? CesmCode);

        private record PqmRow(
            string? Name,
            string? QualType,
            string? CesmCode,
            string? CesmCode1,
            string? MinTimeTotal,
            string? WIL,
            string? Accreditation,
            string? TotalSubsidy);

        private record PqmMatchResult(
            bool HasMatch,
            PqmRow? Row,
            bool NeedsReview,
            string MatchNote,
            string FailureLabel);

        private static PqmMatchResult FindPqmMatch(QualRecord qual, IReadOnlyList<PqmRow> pqmRows)
        {
            var nameRows = pqmRows
                .Where(p => string.Equals(NormName(p.Name), NormName(qual.QualName), StringComparison.Ordinal))
                .ToList();

            if (nameRows.Count == 0)
            {
                return new PqmMatchResult(
                    false,
                    null,
                    false,
                    "Qualification name was not found in PQM Authorised Qualification Name.",
                    "No PQM name match");
            }

            var typeRows = nameRows
                .Where(p => string.Equals(Norm(p.QualType), Norm(qual.QualType), StringComparison.Ordinal))
                .ToList();

            if (typeRows.Count == 0)
            {
                return new PqmMatchResult(
                    false,
                    nameRows[0],
                    false,
                    "Qualification name matched in PQM, but the qualification type did not match on the same PQM row.",
                    "No PQM type match");
            }

            return new PqmMatchResult(
                true,
                typeRows[0],
                false,
                "Matched PQM on qualification name and qualification type. Rule 38 no longer depends on PQM CESM columns.",
                "");
        }

        private static Rule38ValidationRow ValidateQualification(
            int rowNo,
            QualRecord qual,
            PqmMatchResult match,
            string populationType,
            string populationClassificationNote,
            IReadOnlyList<string> heqfCodes)
        {
            var failed = new List<string>();

            if (!match.HasMatch || match.Row == null)
            {
                return new Rule38ValidationRow
                {
                    ValidationNumber = rowNo,
                    QualCode = qual.QualCode,
                    QualName = qual.QualName,
                    ApprovalStatus = qual.ApprovalStatus,
                    QualType = qual.QualType,
                    MinTimeTotal = qual.MinTimeTotal,
                    MinTimeWIL = qual.MinTimeWIL,
                    HeqfIndicator = qual.HeqfIndicator,
                    TotalSubsidy = qual.TotalSubsidy,
                    CesmCode = qual.CesmCode,
                    PopulationType = populationType,
                    PopulationClassificationNote = populationClassificationNote,
                    HasPqmMatch = false,
                    PqmName = match.Row?.Name,
                    PqmQualType = match.Row?.QualType,
                    PqmCesmCode = match.Row?.CesmCode,
                    PqmCesmCode1 = match.Row?.CesmCode1,
                    PqmMinTimeTotal = match.Row?.MinTimeTotal,
                    PqmWIL = match.Row?.WIL,
                    PqmAccreditation = match.Row?.Accreditation,
                    PqmTotalSubsidy = match.Row?.TotalSubsidy,
                    MatchNote = match.MatchNote,
                    ValidationResult = "FAIL",
                    FailedControls = new List<string> { match.FailureLabel, "C2", "C3", "C4", "C5", "C6" }
                };
            }

            var pqm = match.Row;
            var c2 = string.Equals(Norm(qual.QualType), Norm(pqm.QualType), StringComparison.OrdinalIgnoreCase);
            if (!c2) failed.Add("C2");

            var c3 = NumericMatch(qual.MinTimeTotal, pqm.MinTimeTotal);
            if (!c3) failed.Add("C3");

            var c4 = NumericMatch(qual.MinTimeWIL, pqm.WIL);
            if (!c4) failed.Add("C4");

            var expectedHeqf = IsHeqfIndicated(pqm.Accreditation, heqfCodes) ? "Y" : "N";
            var c5 = string.Equals(Norm(qual.HeqfIndicator), expectedHeqf, StringComparison.OrdinalIgnoreCase);
            if (!c5) failed.Add("C5");

            var c6 = NumericMatch(qual.TotalSubsidy, pqm.TotalSubsidy);
            if (!c6) failed.Add("C6");

            return new Rule38ValidationRow
            {
                ValidationNumber = rowNo,
                QualCode = qual.QualCode,
                QualName = qual.QualName,
                ApprovalStatus = qual.ApprovalStatus,
                QualType = qual.QualType,
                MinTimeTotal = qual.MinTimeTotal,
                MinTimeWIL = qual.MinTimeWIL,
                HeqfIndicator = qual.HeqfIndicator,
                TotalSubsidy = qual.TotalSubsidy,
                CesmCode = qual.CesmCode,
                PopulationType = populationType,
                PopulationClassificationNote = populationClassificationNote,
                HasPqmMatch = true,
                PqmName = pqm.Name,
                PqmQualType = pqm.QualType,
                PqmCesmCode = pqm.CesmCode,
                PqmCesmCode1 = pqm.CesmCode1,
                PqmMinTimeTotal = pqm.MinTimeTotal,
                PqmWIL = pqm.WIL,
                PqmAccreditation = pqm.Accreditation,
                PqmTotalSubsidy = pqm.TotalSubsidy,
                NeedsReview = match.NeedsReview,
                MatchNote = match.MatchNote,
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

        private static void NormaliseLoadedSummary(Rule38ValidationSummary? summary)
        {
            if (summary == null)
                return;

            if (!summary.UseMPrefixPopulationSplit && summary.ExcludeMPrefixPattern)
                summary.UseMPrefixPopulationSplit = true;

            if (string.IsNullOrWhiteSpace(summary.PostgraduateTypesCsv))
                summary.PostgraduateTypesCsv = string.Join(",", DefaultPostgraduateTypeCodes);

            var postgraduateTypeCodes = ParsePostgraduateTypeCodes(summary.PostgraduateTypesCsv);
            summary.ValidationRows ??= new List<Rule38ValidationRow>();
            foreach (var row in summary.ValidationRows)
            {
                if (string.IsNullOrWhiteSpace(row.PopulationType))
                {
                    var population = ClassifyPopulationType(
                        row.QualCode,
                        row.QualType,
                        postgraduateTypeCodes,
                        summary.UseMPrefixPopulationSplit);
                    row.PopulationType = population.PopulationType;
                    row.PopulationClassificationNote ??= population.PopulationClassificationNote;
                }
            }

            summary.PostgraduateCount = summary.ValidationRows.Count(r =>
                string.Equals(r.PopulationType, "Postgraduate", StringComparison.OrdinalIgnoreCase));
            summary.UndergraduateCount = summary.ValidationRows.Count - summary.PostgraduateCount;
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
                    "qual_cesm"     => columns.FirstOrDefault(c => c.Equals("_006", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_name"      => columns.FirstOrDefault(c => c.Contains("Authorised", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_type"      => columns.FirstOrDefault(c => c.Contains("HEQF_Qual", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_cesm"      => columns.FirstOrDefault(c => c.Equals("CESM_CODE", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
                    "pqm_cesm1"     => columns.FirstOrDefault(c => c.Equals("CESM_CODE1", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault(),
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
    (SELECT COUNT(*) FROM [{qt}] Q WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50), Q.[{qa}])))) = '{av}') AS ApprovedCount,
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
                var qcc = Sanitise(!string.IsNullOrWhiteSpace(request.QualCesmCodeCol) ? request.QualCesmCodeCol : "_006");
                var pn  = Sanitise(request.PqmNameCol);
                var pt5 = Sanitise(request.PqmQualTypeCol);
                var pc  = Sanitise(request.PqmCesmCodeCol);
                var pc1 = Sanitise(request.PqmCesmCode1Col);
                var p53 = Sanitise(request.PqmMinTimeTotalCol);
                var p54 = Sanitise(request.PqmWilCol);
                var p84 = Sanitise(request.PqmAccreditationCol);
                var p90 = Sanitise(request.PqmTotalSubsidyCol);
                var av  = (request.QualApprovalValue ?? "A").Replace("'", "''").Trim().ToUpperInvariant();
                var useMPrefixPopulationSplit = ResolveUseMPrefixPopulationSplit(
                    request.UseMPrefixPopulationSplit,
                    request.ExcludeMPrefixPattern);
                var postgraduateTypeCodes = ParsePostgraduateTypeCodes(request.PostgraduateTypesCsv);

                // Count totals
                var qualTotal = 0;
                var approvedCount = 0;
                using (var cmd = new SqlCommand(
                    $"SELECT COUNT(*) FROM [{qt}];" +
                    $"SELECT COUNT(*) FROM [{qt}] WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50),[{qa}])))) = '{av}';", conn)
                    .WithLargeDataTimeout())
                {
                    using var r = await cmd.ExecuteReaderAsync();
                    if (await r.ReadAsync()) qualTotal = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0));
                    if (await r.NextResultAsync() && await r.ReadAsync()) approvedCount = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0));
                }

                // Load approved QUAL rows (CESM code read directly from QUAL)
                var qualSql = $@"
SELECT
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), Q.[{qi}])))) AS QualCode,
    LTRIM(RTRIM(CONVERT(nvarchar(500), Q.[{qn}]))) AS QualName,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50),  Q.[{qa}])))) AS ApprovalStatus,
    LTRIM(RTRIM(CONVERT(nvarchar(100), Q.[{qt5}]))) AS QualType,
    LTRIM(RTRIM(CONVERT(nvarchar(50),  Q.[{q53}]))) AS MinTimeTotal,
    LTRIM(RTRIM(CONVERT(nvarchar(50),  Q.[{q54}]))) AS MinTimeWIL,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(10),  Q.[{q84}])))) AS HeqfIndicator,
    LTRIM(RTRIM(CONVERT(nvarchar(50),  Q.[{q90}]))) AS TotalSubsidy,
    LTRIM(RTRIM(CONVERT(nvarchar(100), Q.[{qcc}]))) AS CesmCode
FROM [{qt}] Q
WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50), Q.[{qa}])))) = '{av}'
ORDER BY Q.[{qi}]";

                var pqmSql = $@"
SELECT
    LTRIM(RTRIM(CONVERT(nvarchar(500), P.[{pn}]))) AS PqmName,
    LTRIM(RTRIM(CONVERT(nvarchar(100), P.[{pt5}]))) AS PqmQualType,
    CAST(NULL AS nvarchar(100)) AS PqmCesmCode,
    CAST(NULL AS nvarchar(100)) AS PqmCesmCode1,
    LTRIM(RTRIM(CONVERT(nvarchar(50),  P.[{p53}]))) AS PqmMinTimeTotal,
    LTRIM(RTRIM(CONVERT(nvarchar(50),  P.[{p54}]))) AS PqmWIL,
    LTRIM(RTRIM(CONVERT(nvarchar(500), P.[{p84}]))) AS PqmAccreditation,
    LTRIM(RTRIM(CONVERT(nvarchar(50),  P.[{p90}]))) AS PqmTotalSubsidy
FROM [{pt}] P;";

                var heqfCodes = ParseHeqfCodes(request.HeqfIndicatorCodesCsv);
                var qualRows = new List<QualRecord>();
                using (var cmd = new SqlCommand(qualSql, conn).WithLargeDataTimeout())
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string Read(int i) => reader.IsDBNull(i) ? "" : (reader.GetValue(i)?.ToString() ?? "");
                        string? ReadNullable(int i) => reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString();

                        qualRows.Add(new QualRecord(
                            Read(0),
                            Read(1),
                            Read(2),
                            ReadNullable(3),
                            ReadNullable(4),
                            ReadNullable(5),
                            ReadNullable(6),
                            ReadNullable(7),
                            ReadNullable(8)));
                    }
                }

                var pqmRows = new List<PqmRow>();
                using (var cmd = new SqlCommand(pqmSql, conn).WithLargeDataTimeout())
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string? ReadNullable(int i) => reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString();

                        pqmRows.Add(new PqmRow(
                            ReadNullable(0),
                            ReadNullable(1),
                            ReadNullable(2),
                            ReadNullable(3),
                            ReadNullable(4),
                            ReadNullable(5),
                            ReadNullable(6),
                            ReadNullable(7)));
                    }
                }

                var rows = new List<Rule38ValidationRow>();
                int rowNo = 1;
                foreach (var qual in qualRows)
                {
                    var population = ClassifyPopulationType(qual, postgraduateTypeCodes, useMPrefixPopulationSplit);
                    rows.Add(ValidateQualification(
                        rowNo++,
                        qual,
                        FindPqmMatch(qual, pqmRows),
                        population.PopulationType,
                        population.PopulationClassificationNote,
                        heqfCodes));
                }

                var pqmMatchCount   = rows.Count(r => r.HasPqmMatch);
                var pqmNoMatchCount = rows.Count(r => !r.HasPqmMatch);
                var reviewRequiredCount = rows.Count(r => r.NeedsReview);
                var postgraduateCount = rows.Count(r => string.Equals(r.PopulationType, "Postgraduate", StringComparison.OrdinalIgnoreCase));
                var undergraduateCount = rows.Count - postgraduateCount;
                var overallPass     = rows.Count(r => r.ValidationResult == "PASS");
                var overallFail     = rows.Count(r => r.ValidationResult == "FAIL");
                var total           = rows.Count;
                var rate            = total > 0 ? Math.Round((decimal)overallFail / total * 100, 2) : 0m;

                var controlSummaries = BuildControlSummaries(rows,
                    request.QualTable, request.QualApprovalCol, request.QualApprovalValue ?? "A",
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
                    UndergraduateCount   = undergraduateCount,
                    PostgraduateCount    = postgraduateCount,
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
                    QualApprovalValue    = request.QualApprovalValue ?? "A",
                    QualTypeCol          = request.QualTypeCol,
                    QualMinTimeTotalCol  = request.QualMinTimeTotalCol,
                    QualMinTimeWilCol    = request.QualMinTimeWilCol,
                    QualHeqfCol          = request.QualHeqfCol,
                    QualTotalSubsidyCol  = request.QualTotalSubsidyCol,
                    QualCesmCodeCol      = request.QualCesmCodeCol,
                    PqmTable             = request.PqmTable,
                    PqmNameCol           = request.PqmNameCol,
                    PqmQualTypeCol       = request.PqmQualTypeCol,
                    PqmCesmCodeCol       = request.PqmCesmCodeCol,
                    PqmCesmCode1Col      = request.PqmCesmCode1Col,
                    PqmMinTimeTotalCol   = request.PqmMinTimeTotalCol,
                    PqmWilCol            = request.PqmWilCol,
                    PqmAccreditationCol  = request.PqmAccreditationCol,
                    PqmTotalSubsidyCol   = request.PqmTotalSubsidyCol,
                    ReviewRequiredCount  = reviewRequiredCount,
                    HeqfIndicatorCodesCsv = request.HeqfIndicatorCodesCsv,
                    UseMPrefixPopulationSplit = useMPrefixPopulationSplit,
                    ExcludeMPrefixPattern = useMPrefixPopulationSplit,
                    PostgraduateTypesCsv  = string.Join(",", postgraduateTypeCodes),
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
                    NormaliseLoadedSummary(deserializedSummary);
                }
                catch { }
            }

            int? currentUserId = null;
            var signoffs = new List<RunSignoffViewModel>();
            var daSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            string? currentUserSignoffComment = null;
            bool currentUserHasSigned = false;
            string currentUserRole = "";

            if (!string.IsNullOrWhiteSpace(currentUserEmail))
            {
                currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);
                if (currentUserId.HasValue)
                    currentUserRole = await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? "";
            }

            signoffs = await LoadSignoffsAsync(connection, runId, currentUserId);
            daSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            var currentRoleSignoff = signoffs.FirstOrDefault(s =>
                HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(
                    s.SignoffRole, currentUserRole));
            currentUserHasSigned = currentRoleSignoff != null;
            currentUserSignoffComment = currentRoleSignoff?.Comment;

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
                QualCesmCodeCol        = deserializedSummary?.QualCesmCodeCol ?? "_006",
                PqmTable               = deserializedSummary?.PqmTable ?? "PQM",
                PqmNameCol             = deserializedSummary?.PqmNameCol ?? "Authorised_Qualification_Name",
                PqmQualTypeCol         = deserializedSummary?.PqmQualTypeCol ?? "HEQF_Qual_Type",
                PqmCesmCodeCol         = deserializedSummary?.PqmCesmCodeCol ?? "CESM_CODE",
                PqmCesmCode1Col        = deserializedSummary?.PqmCesmCode1Col ?? "CESM_CODE1",
                PqmMinTimeTotalCol     = deserializedSummary?.PqmMinTimeTotalCol ?? "Total2",
                PqmWilCol              = deserializedSummary?.PqmWilCol ?? "WIL_EL2",
                PqmAccreditationCol    = deserializedSummary?.PqmAccreditationCol ?? "CHE_HEQC_Accreditation_Approval_Ref_Nr",
                PqmTotalSubsidyCol     = deserializedSummary?.PqmTotalSubsidyCol ?? "Total2",
                HeqfIndicatorCodesCsv  = deserializedSummary?.HeqfIndicatorCodesCsv ?? "H/,HEQF,HEQSF",
                UseMPrefixPopulationSplit = deserializedSummary?.UseMPrefixPopulationSplit ?? deserializedSummary?.ExcludeMPrefixPattern ?? false,
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
    c.EngagementName, c.MaconomyNumber,
    ISNULL(vr.HemisServer, '') AS HemisServer,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
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
                    NormaliseLoadedSummary(summary);
                }
                catch { }
            }

            int? currentUserId = null;
            var signoffs = new List<RunSignoffViewModel>();
            var daSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            var currentUserRole = "";
            if (!string.IsNullOrWhiteSpace(currentUserEmail))
            {
                currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);
                if (currentUserId.HasValue)
                    currentUserRole = await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? "";
            }

            signoffs = await LoadSignoffsAsync(connection, runId, currentUserId);
            daSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

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
                    QualCesmCodeCol     = summary.QualCesmCodeCol,
                    PqmTable            = summary.PqmTable,
                    PqmNameCol          = summary.PqmNameCol,
                    PqmQualTypeCol      = summary.PqmQualTypeCol,
                    PqmCesmCodeCol      = summary.PqmCesmCodeCol,
                    PqmCesmCode1Col     = summary.PqmCesmCode1Col,
                    PqmMinTimeTotalCol  = summary.PqmMinTimeTotalCol,
                    PqmWilCol           = summary.PqmWilCol,
                    PqmAccreditationCol = summary.PqmAccreditationCol,
                    PqmTotalSubsidyCol  = summary.PqmTotalSubsidyCol,
                    HeqfIndicatorCodesCsv = summary.HeqfIndicatorCodesCsv,
                    UseMPrefixPopulationSplit = summary.UseMPrefixPopulationSplit || summary.ExcludeMPrefixPattern,
                    ExcludeMPrefixPattern = summary.UseMPrefixPopulationSplit || summary.ExcludeMPrefixPattern,
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
            if (!userId.HasValue)
                throw new InvalidOperationException("Reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("Validation run was not found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            var role = await GetEngagementRoleAsync(connection, clientId.Value, userId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserSignOff(role))
                throw new InvalidOperationException("Only assigned data analysts, managers, and directors can sign off a validation run.");

            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) &&
                !await HasSignoffRoleAsync(connection, runId, "DataAnalyst"))
            {
                throw new InvalidOperationException("The assigned data analyst must sign off before this review can be completed.");
            }

            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
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
            cmd.Parameters.AddWithValue("@ClientID", clientId.Value);
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@ReviewerID", userId.Value);
            cmd.Parameters.AddWithValue("@Comment", (object?)comment ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SignoffRole", role!);
            await cmd.ExecuteNonQueryAsync();

            await UpdateRunStatusFromSignoffsAsync(connection, runId);
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var userId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!userId.HasValue)
                throw new InvalidOperationException("Reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("Validation run was not found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, userId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(
                connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0) return;
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
            var useMPrefixPopulationSplit = ResolveUseMPrefixPopulationSplit(
                request.UseMPrefixPopulationSplit,
                request.ExcludeMPrefixPattern);
            var postgraduateTypeCodes = ParsePostgraduateTypeCodes(request.PostgraduateTypesCsv);
            var postgraduateTypeCodeSql = string.Join(", ",
                postgraduateTypeCodes.Select(code => $"'{code.Replace("'", "''")}'"));
            if (string.IsNullOrWhiteSpace(postgraduateTypeCodeSql))
                postgraduateTypeCodeSql = "'__NO_POSTGRADUATE_CODES__'";

            var populationTypeSql = useMPrefixPopulationSplit
                ? $"CASE WHEN UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), Q.[{qi}])))) LIKE 'M_____' OR UPPER(LTRIM(RTRIM(CONVERT(nvarchar(100), Q.[{qt5}])))) IN ({postgraduateTypeCodeSql}) THEN 'Postgraduate' ELSE 'Undergraduate' END"
                : $"CASE WHEN UPPER(LTRIM(RTRIM(CONVERT(nvarchar(100), Q.[{qt5}])))) IN ({postgraduateTypeCodeSql}) THEN 'Postgraduate' ELSE 'Undergraduate' END";

            return $@"-- HEMIS Rule 38: QUAL -> PQM Validation
-- Rule 38 matches approved QUAL rows to PQM on qualification name and qualification type.
-- Population split: approved QUAL rows remain in scope. Rows are tagged Postgraduate when QUAL.[{qt5}] is in the configured postgraduate list{(useMPrefixPopulationSplit ? " or QUAL code matches M_____" : "")}; all other approved rows are tagged Undergraduate.
-- 5.1.2  Qualification type: [{qt5}] vs PQM.[{pt5}]
-- 5.1.3  Minimum Time Total: [{q53}] vs PQM.[{p53}]
-- 5.1.4  Minimum Time WIL: [{q54}] vs PQM.[{p54}]
-- 5.1.5  HEQF/HEQSF Indicator: [{q84}] (Y/N) vs PQM.[{p84}] using codes: {request.HeqfIndicatorCodesCsv}
-- 5.1.6  Total Subsidy Units: [{q90}] vs PQM.[{p90}]

WITH ApprovedQual AS (
    SELECT
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), Q.[{qi}])))) AS QualCode,
        LTRIM(RTRIM(CONVERT(nvarchar(500), Q.[{qn}]))) AS QualName,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50), Q.[{qa}])))) AS ApprovalStatus,
        LTRIM(RTRIM(CONVERT(nvarchar(100), Q.[{qt5}]))) AS QualType,
        {populationTypeSql} AS PopulationType,
        LTRIM(RTRIM(CONVERT(nvarchar(50), Q.[{q53}]))) AS MinTimeTotal,
        LTRIM(RTRIM(CONVERT(nvarchar(50), Q.[{q54}]))) AS MinTimeWIL,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(10), Q.[{q84}])))) AS HeqfIndicator,
        LTRIM(RTRIM(CONVERT(nvarchar(50), Q.[{q90}]))) AS TotalSubsidy
    FROM [{qt}] Q
    WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(50), Q.[{qa}])))) = '{av}'
)
SELECT
    AQ.QualCode,
    AQ.QualName,
    AQ.ApprovalStatus,
    AQ.QualType,
    AQ.PopulationType,
    PQM.[{pt5}] AS PQM_QualType,
    PQM.[{p53}] AS PQM_MinTimeTotal,
    PQM.[{p54}] AS PQM_WIL,
    PQM.[{p84}] AS PQM_Accreditation,
    PQM.[{p90}] AS PQM_TotalSubsidy,
    CASE
        WHEN PQM.[{pn}] IS NULL THEN 'FAIL'
        ELSE 'PASS'
    END AS PQM_MatchStatus,
    CASE WHEN UPPER(LTRIM(RTRIM(CONVERT(nvarchar(100), AQ.QualType)))) = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(100), PQM.[{pt5}])))) THEN 'PASS' ELSE 'FAIL' END AS C2_TypeMatch,
    CASE WHEN TRY_CONVERT(decimal(18,4), NULLIF(AQ.MinTimeTotal, N'')) = TRY_CONVERT(decimal(18,4), NULLIF(CONVERT(nvarchar(50), PQM.[{p53}]), N'')) THEN 'PASS' ELSE 'FAIL' END AS C3_MinTimeMatch,
    CASE WHEN TRY_CONVERT(decimal(18,4), NULLIF(AQ.MinTimeWIL, N'')) = TRY_CONVERT(decimal(18,4), NULLIF(CONVERT(nvarchar(50), PQM.[{p54}]), N'')) THEN 'PASS' ELSE 'FAIL' END AS C4_WILMatch,
    AQ.HeqfIndicator,
    AQ.TotalSubsidy
FROM ApprovedQual AQ
OUTER APPLY (
    SELECT TOP (1) P.*
    FROM [{pt}] P
    WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(500), P.[{pn}])))) = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(500), AQ.QualName))))
      AND UPPER(LTRIM(RTRIM(CONVERT(nvarchar(100), P.[{pt5}])))) = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(100), AQ.QualType))))
    ORDER BY P.[{pn}]
) PQM
ORDER BY AQ.QualCode;".Trim();
        }

        // ── System DB helpers ─────────────────────────────────────────────────

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

            var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            return conn;
        }

        private static async Task<int?> GetSystemUserIdByEmailAsync(SqlConnection conn, string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            cmd.Parameters.AddWithValue("@Email", email);
            var val = await cmd.ExecuteScalarAsync();
            return val == null || val == DBNull.Value ? null : Convert.ToInt32(val);
        }

        private static async Task EnsureClientNotArchivedAsync(SqlConnection conn, int clientId)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 Status FROM dbo.Clients WHERE ClientID = @ClientID;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            var val = Convert.ToString(await cmd.ExecuteScalarAsync());
            if (string.Equals(val, "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This engagement is archived and cannot be modified.");
        }

        private static async Task ClearRuleSignoffsAsync(SqlConnection conn, int clientId, int ruleNumber)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
DELETE rs FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.ValidationRuns r ON r.RunID = rs.RunID
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
    (@ClientID, @UserID, 38, 'Enhanced QUAL -> PQM Validation', @UserName,
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

        private static async Task<List<RunSignoffViewModel>> LoadSignoffsAsync(SqlConnection conn, int runId, int? currentUserId)
        {
            var signoffs = new List<RunSignoffViewModel>();
            await using var cmd = conn.CreateConfiguredCommand();
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
            while (await r.ReadAsync())
            {
                signoffs.Add(new RunSignoffViewModel
                {
                    Id            = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0)),
                    SignoffRole   = r.IsDBNull(1) ? "" : r.GetString(1),
                    ReviewerName  = r.IsDBNull(2) ? "" : r.GetString(2),
                    ReviewerEmail = r.IsDBNull(3) ? "" : r.GetString(3),
                    Comment       = r.IsDBNull(4) ? "" : r.GetString(4),
                    SignedOffAt   = r.IsDBNull(5) ? DateTime.UtcNow : r.GetDateTime(5),
                    IsCurrentUser = !r.IsDBNull(6) && r.GetBoolean(6)
                });
            }
            return signoffs;
        }

        private static async Task<string?> GetEngagementRoleAsync(SqlConnection conn, int clientId, int userId)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT TOP 1 EngagementRole
FROM dbo.UserClientAssignments
WHERE ClientID = @ClientID AND UserID = @UserID;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            cmd.Parameters.AddWithValue("@UserID", userId);
            var val = await cmd.ExecuteScalarAsync();
            return val == null || val == DBNull.Value ? null : Convert.ToString(val);
        }

        private static async Task UpdateRunStatusFromSignoffsAsync(SqlConnection conn, int runId)
        {
            var hasAllSignoffs = await HasAllRequiredSignoffsAsync(conn, runId);
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
UPDATE dbo.ValidationRuns SET Status = @Status WHERE RunID = @RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@Status", hasAllSignoffs ? "Reviewed and Completed" : "Needs Review");
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<bool> HasAllRequiredSignoffsAsync(SqlConnection conn, int runId)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'DataAnalyst') THEN 1 ELSE 0 END AS HasDA,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Manager') THEN 1 ELSE 0 END AS HasMgr,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Director') THEN 1 ELSE 0 END AS HasDir;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return false;
            return (!reader.IsDBNull(0) && reader.GetInt32(0) == 1) &&
                   (!reader.IsDBNull(1) && reader.GetInt32(1) == 1) &&
                   (!reader.IsDBNull(2) && reader.GetInt32(2) == 1);
        }

        private static async Task<bool> HasSignoffRoleAsync(SqlConnection conn, int runId, string signoffRole)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = @SignoffRole;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@SignoffRole", signoffRole);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        private static async Task<bool> IsWorkspaceSavedAsync(SqlConnection conn, int runId)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM dbo.ValidationRuns
    WHERE RunID = @RunID
      AND (WorkspaceSavedAt IS NOT NULL
           OR EXISTS (SELECT 1 FROM dbo.ReviewSignoffs rs
                      WHERE rs.RunID = ValidationRuns.RunID AND rs.SignoffRole = 'DataAnalyst'))
) THEN 1 ELSE 0 END;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        private static string ComputeHash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
