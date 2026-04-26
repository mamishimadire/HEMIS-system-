using ClosedXML.Excel;
using System.Text;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IExportService
    {
        byte[] ExportExcel(ValidationSummary summary);
        byte[] ExportExcel(Rule34ValidationSummary summary);
        byte[] ExportCsv(ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportCsv(Rule34ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportSql(string sql);
    }

    public class ExportService : IExportService
    {
        // ─── Excel Export — mirrors notebook Excel with 3 sheets ─────────────
        public byte[] ExportExcel(ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            // ── Sheet 1: Validation Results ──
            var wsResults = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsResults, 1, "RULE 36 VALIDATION RESULTS", 4);
            WriteValidationHeaderRow(wsResults, 2, summary);
            WriteValidationRows(wsResults, 3, summary);

            // ── Sheet 2: Summary ──
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 36: DECEASED STUDENTS VALIDATION", 2);

            var summaryData = new[]
            {
                ("Database",            summary.Database),
                ("STUD Table",          summary.StudTable),
                ("Deceased Table",      summary.DeceasedTable),
                ("STUD Column",         summary.StudColumn),
                ("Deceased Column",     summary.DeceasedColumn),
                ("Validation Rule",     $"[{summary.StudTable}].[{summary.StudColumn}] = [{summary.DeceasedTable}].[{summary.DeceasedColumn}]"),
                ("Validation Date",     summary.Timestamp),
                ("",                    ""),
                ("VALIDATION RESULTS",  ""),
                ("Total Validated",     summary.TotalValidated.ToString("N0")),
                ("PASS (Active)",       summary.PassCount.ToString("N0")),
                ("FAIL (Deceased)",     summary.FailCount.ToString("N0")),
                ("Exception Rate",      $"{summary.ExceptionRate:F2}%"),
                ("Status",              summary.Status),
            };

            int row = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                {
                    var hdrCell = wsSummary.Cell(row, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(row, 1, row, 2).Merge();
                }
                else if (label == "")
                {
                    // blank separator
                }
                else
                {
                    wsSummary.Cell(row, 1).Value = label;
                    wsSummary.Cell(row, 1).Style.Font.Bold = true;
                    wsSummary.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(row, 2).Value = value;

                    if (label == "Status")
                    {
                        var color = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(row, 2).Style.Fill.BackgroundColor = color;
                        wsSummary.Cell(row, 2).Style.Font.Bold = true;
                    }
                }
                row++;
            }

            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 60;

            // ── Sheet 3: Exceptions ──
            if (summary.Exceptions.Any())
            {
                var wsEx = wb.Worksheets.Add("Exceptions");
                StyleHeaderRow(wsEx, 1, "RULE 36 EXCEPTIONS — DECEASED STUDENTS", 5);

                var exHeaders = new[] { "Validation #", "Student ID", "Exception Reason", "Validation Result", "Additional Details" };
                for (int i = 0; i < exHeaders.Length; i++)
                {
                    var cell = wsEx.Cell(2, i + 1);
                    cell.Value = exHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    cell.Style.Font.FontColor = XLColor.White;
                }

                int exRow = 3;
                foreach (var ex in summary.Exceptions)
                {
                    wsEx.Cell(exRow, 1).Value = ex.ValidationNumber;
                    wsEx.Cell(exRow, 2).Value = ex.StudentId;
                    wsEx.Cell(exRow, 3).Value = ex.ExceptionReason;
                    wsEx.Cell(exRow, 4).Value = ex.ValidationResult;
                    wsEx.Cell(exRow, 5).Value = string.Join(" | ", ex.AdditionalColumns.Take(5).Select(kv => $"{kv.Key}={kv.Value}"));

                    // Red fill for FAIL rows
                    wsEx.Range(exRow, 1, exRow, 5).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3F3");
                    wsEx.Cell(exRow, 4).Style.Font.FontColor = XLColor.FromHtml("#C62828");
                    wsEx.Cell(exRow, 4).Style.Font.Bold = true;
                    exRow++;
                }

                for (int c = 1; c <= 5; c++) wsEx.Column(c).AdjustToContents();
            }

            // ── Sheet 4: Statistics ──
            var wsStats = wb.Worksheets.Add("Statistics");
            StyleHeaderRow(wsStats, 1, "VALIDATION STATISTICS", 2);

            var stats = new[]
            {
                ("Total Records Validated", (object)summary.TotalValidated),
                ("PASS Count",              (object)summary.PassCount),
                ("FAIL Count",              (object)summary.FailCount),
                ("Exception Rate (%)",      (object)(double)summary.ExceptionRate),
                ("Pass Rate (%)",           (object)(double)(summary.TotalValidated > 0
                    ? Math.Round((decimal)summary.PassCount / summary.TotalValidated * 100, 2) : 0)),
            };

            row = 2;
            foreach (var (label, value) in stats)
            {
                wsStats.Cell(row, 1).Value = label;
                wsStats.Cell(row, 1).Style.Font.Bold = true;
                wsStats.Cell(row, 2).SetValue(value?.ToString());
                row++;
            }
            wsStats.Column(1).Width = 30;
            wsStats.Column(2).Width = 20;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule34ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var wsResults = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsResults, 1, "RULE 34 VALIDATION RESULTS", 7);
            var resultHeaders = new[]
            {
                "Validation Number",
                "First Day",
                "Last Day",
                "Prepared Census Date",
                "Stored Census Date",
                "Day Status",
                "Validation Status"
            };

            for (int i = 0; i < resultHeaders.Length; i++)
            {
                var cell = wsResults.Cell(2, i + 1);
                cell.Value = resultHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var rowIndex = 3;
            foreach (var row in summary.ValidationRows)
            {
                wsResults.Cell(rowIndex, 1).Value = row.ValidationNumber;
                wsResults.Cell(rowIndex, 2).Value = row.FirstDayValue;
                wsResults.Cell(rowIndex, 3).Value = row.LastDayValue;
                wsResults.Cell(rowIndex, 4).Value = row.ComputedCensusDate;
                wsResults.Cell(rowIndex, 5).Value = row.CensusDateValue;
                wsResults.Cell(rowIndex, 6).Value = row.DayStatus;
                wsResults.Cell(rowIndex, 7).Value = row.ValidationStatus;

                wsResults.Range(rowIndex, 1, rowIndex, 7).Style.Fill.BackgroundColor =
                    row.DateMatch ? XLColor.FromHtml("#F3FFF3") : XLColor.FromHtml("#FFF3F3");
                rowIndex++;
            }

            for (int c = 1; c <= 7; c++) wsResults.Column(c).AdjustToContents();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 34: CENSUS DATE VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("Source Table", summary.TableName),
                ("First Day Column", summary.FirstDayColumn),
                ("Last Day Column", summary.LastDayColumn),
                ("Census Date Column", summary.CensusDateColumn),
                ("Holiday Years", summary.HolidayYearRange),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("VALIDATION RESULTS", ""),
                ("Total Validated", summary.TotalValidated.ToString("N0")),
                ("PASS (Match)", summary.PassCount.ToString("N0")),
                ("FAIL (Mismatch)", summary.FailCount.ToString("N0")),
                ("Mismatch Rate", $"{summary.ExceptionRate:F2}%"),
                ("Computed Holidays", summary.HolidayCount.ToString("N0")),
                ("Computed Weekends", summary.WeekendCount.ToString("N0")),
                ("Status", summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                {
                    var hdrCell = wsSummary.Cell(summaryRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(summaryRow, 1).Value = label;
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = value;

                    if (label == "Status")
                    {
                        var color = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = color;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }

                summaryRow++;
            }

            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 60;

            var wsExceptions = wb.Worksheets.Add("Exceptions");
            StyleHeaderRow(wsExceptions, 1, "RULE 34 EXCEPTIONS", 7);
            for (int i = 0; i < resultHeaders.Length; i++)
            {
                var cell = wsExceptions.Cell(2, i + 1);
                cell.Value = resultHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var exceptionRow = 3;
            foreach (var row in summary.Exceptions)
            {
                wsExceptions.Cell(exceptionRow, 1).Value = row.ValidationNumber;
                wsExceptions.Cell(exceptionRow, 2).Value = row.FirstDayValue;
                wsExceptions.Cell(exceptionRow, 3).Value = row.LastDayValue;
                wsExceptions.Cell(exceptionRow, 4).Value = row.ComputedCensusDate;
                wsExceptions.Cell(exceptionRow, 5).Value = row.CensusDateValue;
                wsExceptions.Cell(exceptionRow, 6).Value = row.DayStatus;
                wsExceptions.Cell(exceptionRow, 7).Value = row.ValidationStatus;
                wsExceptions.Range(exceptionRow, 1, exceptionRow, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3F3");
                exceptionRow++;
            }

            for (int c = 1; c <= 7; c++) wsExceptions.Column(c).AdjustToContents();

            var wsStats = wb.Worksheets.Add("Statistics");
            StyleHeaderRow(wsStats, 1, "VALIDATION STATISTICS", 2);
            var stats = new[]
            {
                ("Total Records Validated", (object)summary.TotalValidated),
                ("PASS Count", (object)summary.PassCount),
                ("FAIL Count", (object)summary.FailCount),
                ("Mismatch Rate (%)", (object)(double)summary.ExceptionRate),
                ("Holiday Count", (object)summary.HolidayCount),
                ("Weekend Count", (object)summary.WeekendCount)
            };

            var statsRow = 2;
            foreach (var (label, value) in stats)
            {
                wsStats.Cell(statsRow, 1).Value = label;
                wsStats.Cell(statsRow, 1).Style.Font.Bold = true;
                wsStats.Cell(statsRow, 2).SetValue(value?.ToString());
                statsRow++;
            }

            wsStats.Column(1).Width = 30;
            wsStats.Column(2).Width = 20;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // ─── CSV Export ───────────────────────────────────────────────────────
        public byte[] ExportCsv(ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();

            if (exceptionsOnly)
            {
                sb.AppendLine("Validation_Number,Student_ID,Exception_Reason,Validation_Result");
                foreach (var ex in summary.Exceptions)
                {
                    sb.AppendLine($"{ex.ValidationNumber},{CsvEscape(ex.StudentId)},{CsvEscape(ex.ExceptionReason)},{ex.ValidationResult}");
                }
            }
            else
            {
                var extraHeaders = GetAdditionalHeaders(summary);
                sb.AppendLine(string.Join(",",
                    new[] { "Validation_Number", "Validation_Result", "Exception_Reason", "Student_ID" }
                        .Concat(extraHeaders)));

                foreach (var row in summary.ValidationRows)
                {
                    var values = new List<string>
                    {
                        row.ValidationNumber.ToString(),
                        CsvEscape(row.ValidationResult),
                        CsvEscape(row.ExceptionReason),
                        CsvEscape(row.StudentId)
                    };

                    foreach (var header in extraHeaders)
                        values.Add(CsvEscape(row.AdditionalColumns.TryGetValue(header, out var value) ? value : null));

                    sb.AppendLine(string.Join(",", values));
                }
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule34ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Validation_Number,First_Day,Last_Day,Prepared_Census_Date,Stored_Census_Date,Day_Status,Validation_Status");

            var rows = exceptionsOnly ? summary.Exceptions : summary.ValidationRows;
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(",",
                    row.ValidationNumber,
                    CsvEscape(row.FirstDayValue),
                    CsvEscape(row.LastDayValue),
                    CsvEscape(row.ComputedCensusDate),
                    CsvEscape(row.CensusDateValue),
                    CsvEscape(row.DayStatus),
                    CsvEscape(row.ValidationStatus)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── SQL Export ───────────────────────────────────────────────────────
        public byte[] ExportSql(string sql) => Encoding.UTF8.GetBytes(sql);

        // ─── Helpers ──────────────────────────────────────────────────────────
        private static void StyleHeaderRow(IXLWorksheet ws, int row, string title, int colSpan)
        {
            ws.Range(row, 1, row, colSpan).Merge();
            var cell = ws.Cell(row, 1);
            cell.Value = title;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 14;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        private static void WriteValidationHeaderRow(IXLWorksheet ws, int row, ValidationSummary summary)
        {
            var headers = new[] { "Validation Number", "Validation Result", "Exception Reason", "Student ID" }
                .Concat(GetAdditionalHeaders(summary))
                .ToList();

            for (int i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteValidationRows(IXLWorksheet ws, int startRow, ValidationSummary summary)
        {
            var headers = GetAdditionalHeaders(summary);
            var rowIndex = startRow;

            foreach (var row in summary.ValidationRows)
            {
                ws.Cell(rowIndex, 1).Value = row.ValidationNumber;
                ws.Cell(rowIndex, 2).Value = row.ValidationResult;
                ws.Cell(rowIndex, 3).Value = row.ExceptionReason ?? "";
                ws.Cell(rowIndex, 4).Value = row.StudentId;

                for (int i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    row.AdditionalColumns.TryGetValue(header, out var value);
                    ws.Cell(rowIndex, 5 + i).Value = value ?? "";
                }

                if (row.ValidationResult == "FAIL")
                    ws.Range(rowIndex, 1, rowIndex, 4 + headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3F3");
                else if (row.ValidationResult == "PASS")
                    ws.Range(rowIndex, 1, rowIndex, 4 + headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#F3FFF3");

                rowIndex++;
            }

            for (int c = 1; c <= 4 + headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static List<string> GetAdditionalHeaders(ValidationSummary summary) =>
            summary.ValidationRows
                .SelectMany(r => r.AdditionalColumns.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

        private static string CsvEscape(string? val)
        {
            if (string.IsNullOrEmpty(val))
                return "";
            if (val.Contains(',') || val.Contains('"') || val.Contains('\n'))
                return $"\"{val.Replace("\"", "\"\"")}\"";
            return val;
        }
    }
}
