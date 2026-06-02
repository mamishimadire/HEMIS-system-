using ClosedXML.Excel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IExportService
    {
        byte[] ExportRule36Excel(ValidationSummary summary);
        byte[] ExportRule37Excel(Rule37ValidationSummary summary);
        byte[] ExportRule37Csv(Rule37ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule11Excel(Rule11ValidationSummary summary);
        byte[] ExportRule11Csv(Rule11ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule54Excel(Rule54ValidationSummary summary);
        byte[] ExportRule54Csv(Rule54ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule55Excel(Rule55ValidationSummary summary);
        byte[] ExportRule55Csv(Rule55ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule57Excel(Rule57ValidationSummary summary);
        byte[] ExportRule57Csv(Rule57ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule58Excel(Rule58ValidationSummary summary);
        byte[] ExportRule58Csv(Rule58ValidationSummary summary);
        byte[] ExportRule59Excel(Rule59ValidationSummary summary);
        byte[] ExportRule59Csv(Rule59ValidationSummary summary);
        byte[] ExportRule38Excel(Rule38ValidationSummary summary);
        byte[] ExportRule38Csv(Rule38ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportExcel(Rule10ValidationSummary summary);
        byte[] ExportExcel(Rule11ValidationSummary summary);
        byte[] ExportExcel(Rule12ValidationSummary summary);
        byte[] ExportExcel(Rule34ValidationSummary summary);
        byte[] ExportExcel(Rule35ValidationSummary summary);
        byte[] ExportExcel(Rule26ValidationSummary summary);
        byte[] ExportExcel(Rule27ValidationSummary summary);
        byte[] ExportExcel(Rule13ValidationSummary summary);
        byte[] ExportExcel(Rule20ValidationSummary summary);
        byte[] ExportExcel(Rule17ValidationSummary summary);
        byte[] ExportExcel(Rule14ValidationSummary summary);
        byte[] ExportExcel(Rule15ValidationSummary summary);
        byte[] ExportExcel(Rule16ValidationSummary summary);
        byte[] ExportExcel(Rule18ValidationSummary summary);
        byte[] ExportExcel(Rule19ValidationSummary summary);
        byte[] ExportExcel(Rule22ValidationSummary summary);
        byte[] ExportExcel(Rule23ValidationSummary summary);
        byte[] ExportExcel(Rule24ValidationSummary summary);
        byte[] ExportExcel(Rule25ValidationSummary summary);
        byte[] ExportExcel(Rule21ValidationSummary summary);
        byte[] ExportExcel(Rule29ValidationSummary summary);
        byte[] ExportExcel(Rule31ValidationSummary summary);
        byte[] ExportExcel(Rule32ValidationSummary summary);
        byte[] ExportRule36Csv(ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportCsv(Rule10ValidationSummary summary);
        byte[] ExportCsv(Rule11ValidationSummary summary);
        byte[] ExportCsv(Rule12ValidationSummary summary);
        byte[] ExportCsv(Rule34ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportCsv(Rule35ValidationSummary summary);
        byte[] ExportCsv(Rule26ValidationSummary summary);
        byte[] ExportCsv(Rule27ValidationSummary summary);
        byte[] ExportCsv(Rule13ValidationSummary summary);
        byte[] ExportCsv(Rule20ValidationSummary summary);
        byte[] ExportCsv(Rule17ValidationSummary summary);
        byte[] ExportCsv(Rule14ValidationSummary summary);
        byte[] ExportCsv(Rule15ValidationSummary summary);
        byte[] ExportCsv(Rule16ValidationSummary summary);
        byte[] ExportCsv(Rule18ValidationSummary summary);
        byte[] ExportCsv(Rule19ValidationSummary summary);
        byte[] ExportCsv(Rule22ValidationSummary summary);
        byte[] ExportCsv(Rule23ValidationSummary summary);
        byte[] ExportCsv(Rule24ValidationSummary summary);
        byte[] ExportCsv(Rule25ValidationSummary summary);
        byte[] ExportCsv(Rule21ValidationSummary summary);
        byte[] ExportCsv(Rule29ValidationSummary summary);
        byte[] ExportCsv(Rule31ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportCsv(Rule32ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule39Excel(Rule39ValidationSummary summary);
        byte[] ExportRule39Csv(Rule39ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportRule51Excel(Rule51ValidationSummary summary);
        byte[] ExportRule51Csv(Rule51ValidationSummary summary);
        byte[] ExportRule52Excel(Rule52ValidationSummary summary);
        byte[] ExportRule52Csv(Rule52ValidationSummary summary);
        byte[] ExportRule53Excel(Rule53ValidationSummary summary);
        byte[] ExportRule53Csv(Rule53ValidationSummary summary);
        byte[] ExportSql(string sql);
    }

    public class ExportService : IExportService
    {
        private const int Rule18DetailedSheetRowThreshold = 10000;

        // ─── Excel Export — mirrors notebook Excel with 3 sheets ─────────────
        public byte[] ExportRule36Excel(ValidationSummary summary)
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

            var hasBlock = !string.IsNullOrWhiteSpace(summary.BlockColumn);
            var colCount = hasBlock ? 11 : 10;

            var wsResults = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsResults, 1, "RULE 34 VALIDATION RESULTS", colCount);

            var resultHeaderList = new List<string>
            {
                "Validation Number",
                "First Day", "Last Day", "c_Days", "c_Days_2",
                "Prepared Census Date", "Actual Census Date", "Stored Census Date",
                "Day Status", "Validation Status"
            };
            if (hasBlock) resultHeaderList.Insert(1, $"{summary.BlockColumn} (block)");
            var resultHeaders = resultHeaderList.ToArray();

            for (int i = 0; i < resultHeaders.Length; i++)
            {
                var cell = wsResults.Cell(2, i + 1);
                cell.Value = resultHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(i == 1 && hasBlock ? "#5D4037" : "#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var rowIndex = 3;
            foreach (var row in summary.ValidationRows)
            {
                int col = 1;
                wsResults.Cell(rowIndex, col++).Value = row.ValidationNumber;
                if (hasBlock) wsResults.Cell(rowIndex, col++).Value = row.BlockValue;
                wsResults.Cell(rowIndex, col++).Value = row.FirstDayValue;
                wsResults.Cell(rowIndex, col++).Value = row.LastDayValue;
                wsResults.Cell(rowIndex, col++).Value = row.CurrentDays?.ToString() ?? "NULL";
                wsResults.Cell(rowIndex, col++).Value = row.CurrentDaysHalf?.ToString() ?? "NULL";
                wsResults.Cell(rowIndex, col++).Value = row.ComputedCensusDate;
                wsResults.Cell(rowIndex, col++).Value = row.ActualCensusDate;
                wsResults.Cell(rowIndex, col++).Value = row.CensusDateValue;
                wsResults.Cell(rowIndex, col++).Value = row.DayStatus;
                wsResults.Cell(rowIndex, col++).Value = row.ValidationStatus;

                wsResults.Range(rowIndex, 1, rowIndex, colCount).Style.Fill.BackgroundColor =
                    row.DateMatch ? XLColor.FromHtml("#F3FFF3") : XLColor.FromHtml("#FFF3F3");
                rowIndex++;
            }

            for (int c = 1; c <= colCount; c++) wsResults.Column(c).AdjustToContents();

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
            StyleHeaderRow(wsExceptions, 1, "RULE 34 EXCEPTIONS", 10);
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
                wsExceptions.Cell(exceptionRow, 4).Value = row.CurrentDays?.ToString() ?? "NULL";
                wsExceptions.Cell(exceptionRow, 5).Value = row.CurrentDaysHalf?.ToString() ?? "NULL";
                wsExceptions.Cell(exceptionRow, 6).Value = row.ComputedCensusDate;
                wsExceptions.Cell(exceptionRow, 7).Value = row.ActualCensusDate;
                wsExceptions.Cell(exceptionRow, 8).Value = row.CensusDateValue;
                wsExceptions.Cell(exceptionRow, 9).Value = row.DayStatus;
                wsExceptions.Cell(exceptionRow, 10).Value = row.ValidationStatus;
                wsExceptions.Range(exceptionRow, 1, exceptionRow, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3F3");
                exceptionRow++;
            }

            for (int c = 1; c <= 10; c++) wsExceptions.Column(c).AdjustToContents();

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

        public byte[] ExportExcel(Rule35ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule35Headers(summary);

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 35: DUPLICATE CHECK ON dbo_CRSE", 2);

            var duplicateRule = string.IsNullOrWhiteSpace(summary.DuplicateColumn)
                ? "COUNT(selected field) = 1 => PASS | COUNT(selected field) > 1 => FAIL"
                : $"COUNT([{summary.DuplicateColumn}]) = 1 => PASS | COUNT([{summary.DuplicateColumn}]) > 1 => FAIL";

            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("Source Table", summary.TableName),
                ("Duplicate Field", summary.DuplicateColumn),
                ("Validation Rule", duplicateRule),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("VALIDATION RESULTS", ""),
                ("Total Validated", summary.TotalValidated.ToString("N0")),
                ("PASS (Unique Records)", summary.PassCount.ToString("N0")),
                ("FAIL (Duplicate Records)", summary.FailCount.ToString("N0")),
                ("Unique Values", summary.UniqueValues.ToString("N0")),
                ("Duplicate Values", summary.DuplicateValues.ToString("N0")),
                ("Records with Duplicates", summary.DuplicateRecords.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
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
                        var color = string.Equals(value, "PASS", StringComparison.OrdinalIgnoreCase)
                            ? XLColor.FromHtml("#C8E6C9")
                            : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = color;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 70;

            var wsResults = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsResults, 1, "RULE 35 VALIDATION RESULTS", headers.Count);
            WriteRule35HeaderRow(wsResults, 2, headers);
            WriteRule35Rows(wsResults, 3, summary.ValidationRows, headers);

            var wsGroups = wb.Worksheets.Add("Duplicate Groups");
            StyleHeaderRow(wsGroups, 1, "RULE 35 DUPLICATE GROUPS", 2);
            wsGroups.Cell(2, 1).Value = "Duplicate Value";
            wsGroups.Cell(2, 2).Value = "Occurrence Count";
            for (var col = 1; col <= 2; col++)
            {
                var cell = wsGroups.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.DuplicateSummary.Count; i++)
            {
                var item = summary.DuplicateSummary[i];
                wsGroups.Cell(i + 3, 1).Value = item.Value;
                wsGroups.Cell(i + 3, 2).Value = item.Count;
            }
            wsGroups.Column(1).AdjustToContents();
            wsGroups.Column(2).AdjustToContents();

            var duplicateRows = summary.ValidationRows
                .Where(row => string.Equals(row.DuplicateStatus, "DUPLICATE", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (duplicateRows.Count > 0)
            {
                var wsExceptions = wb.Worksheets.Add("Exceptions");
                StyleHeaderRow(wsExceptions, 1, "RULE 35 DUPLICATE RECORDS", headers.Count);
                WriteRule35HeaderRow(wsExceptions, 2, headers);
                WriteRule35Rows(wsExceptions, 3, duplicateRows, headers);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule26ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 26: DBO_PROF TO PAYROLL_SAMPLE 5-CONTROL VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("PROF Table", summary.ProfTable),
                ("Payroll Table", summary.PayrollTable),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("POPULATION SUMMARY", ""),
                ("PROF Records", summary.ProfRecordCount.ToString("N0")),
                ("Payroll Records", summary.PayrollRecordCount.ToString("N0")),
                ("Linked Records", summary.LinkedRecordCount.ToString("N0")),
                ("Total Control Tests", summary.TotalValidated.ToString("N0")),
                ("Total Exceptions", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "POPULATION SUMMARY")
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
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 34;
            wsSummary.Column(2).Width = 72;

            var wsControlSummary = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsControlSummary, 1, "RULE 26 CONTROL SUMMARY", 6);
            var controlHeaders = new[] { "Direction", "Control #", "Control Name", "Total Tested", "Exceptions", "Status" };
            for (var i = 0; i < controlHeaders.Length; i++)
            {
                var cell = wsControlSummary.Cell(2, i + 1);
                cell.Value = controlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var controlRow = 3;
            foreach (var direction in summary.Directions)
            {
                foreach (var control in direction.Controls)
                {
                    wsControlSummary.Cell(controlRow, 1).Value = direction.DirectionLabel;
                    wsControlSummary.Cell(controlRow, 2).Value = control.ControlNumber;
                    wsControlSummary.Cell(controlRow, 3).Value = control.ControlName;
                    wsControlSummary.Cell(controlRow, 4).Value = control.TotalTested;
                    wsControlSummary.Cell(controlRow, 5).Value = control.ExceptionCount;
                    wsControlSummary.Cell(controlRow, 6).Value = control.Passed ? "PASS" : "FAIL";
                    controlRow++;
                }
            }
            for (var c = 1; c <= controlHeaders.Length; c++) wsControlSummary.Column(c).AdjustToContents();

            var exceptionHeaders = new[] { "Direction", "Control #", "Control Name", "Personnel Number", "Personnel Name", "Exception Reason", "Base Value", "Reference Value" };
            var wsExceptions = wb.Worksheets.Add("Combined Exceptions");
            StyleHeaderRow(wsExceptions, 1, "RULE 26 EXCEPTIONS", exceptionHeaders.Length);
            for (var i = 0; i < exceptionHeaders.Length; i++)
            {
                var cell = wsExceptions.Cell(2, i + 1);
                cell.Value = exceptionHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var exceptionRow = 3;
            foreach (var exception in summary.Exceptions)
            {
                wsExceptions.Cell(exceptionRow, 1).Value = exception.DirectionLabel;
                wsExceptions.Cell(exceptionRow, 2).Value = exception.ControlNumber;
                wsExceptions.Cell(exceptionRow, 3).Value = exception.ControlName;
                wsExceptions.Cell(exceptionRow, 4).Value = exception.PersonnelNumber;
                wsExceptions.Cell(exceptionRow, 5).Value = exception.PersonnelName;
                wsExceptions.Cell(exceptionRow, 6).Value = exception.ExceptionReason;
                wsExceptions.Cell(exceptionRow, 7).Value = exception.BaseValue;
                wsExceptions.Cell(exceptionRow, 8).Value = exception.ReferenceValue;
                wsExceptions.Range(exceptionRow, 1, exceptionRow, exceptionHeaders.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3F3");
                exceptionRow++;
            }
            for (var c = 1; c <= exceptionHeaders.Length; c++) wsExceptions.Column(c).AdjustToContents();

            foreach (var direction in summary.Directions)
            {
                var sheetName = direction.DirectionKey == "prof_to_payroll" ? "PROF to Payroll" : direction.DirectionLabel;
                var wsDirection = wb.Worksheets.Add(sheetName);
                StyleHeaderRow(wsDirection, 1, $"{direction.DirectionLabel} EXCEPTIONS", exceptionHeaders.Length);
                for (var i = 0; i < exceptionHeaders.Length; i++)
                {
                    var cell = wsDirection.Cell(2, i + 1);
                    cell.Value = exceptionHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    cell.Style.Font.FontColor = XLColor.White;
                }

                var row = 3;
                foreach (var exception in direction.Exceptions)
                {
                    wsDirection.Cell(row, 1).Value = exception.DirectionLabel;
                    wsDirection.Cell(row, 2).Value = exception.ControlNumber;
                    wsDirection.Cell(row, 3).Value = exception.ControlName;
                    wsDirection.Cell(row, 4).Value = exception.PersonnelNumber;
                    wsDirection.Cell(row, 5).Value = exception.PersonnelName;
                    wsDirection.Cell(row, 6).Value = exception.ExceptionReason;
                    wsDirection.Cell(row, 7).Value = exception.BaseValue;
                    wsDirection.Cell(row, 8).Value = exception.ReferenceValue;
                    row++;
                }
                for (var c = 1; c <= exceptionHeaders.Length; c++) wsDirection.Column(c).AdjustToContents();
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule29ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule29Headers(summary);
            var sampleSheetName = summary.Sampled ? "Displayed Sample" : "Matching Records";

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 29: SINGLE COLUMN FILTER", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("Source Table", summary.TableName),
                ("Filter Column", summary.FilterColumn),
                ("Filter Value", summary.FilterValue),
                ("Breakdown Column", string.IsNullOrWhiteSpace(summary.BreakdownColumn) ? "(not selected)" : summary.BreakdownColumn),
                ("Sample Size", summary.SampleSize.ToString("N0")),
                ("Show All Records", summary.ShowAllRecords ? "Yes" : "No"),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("100% Source Records Tested", summary.TotalValidated.ToString("N0")),
                ("100% Matching Records Found", summary.MatchingCount.ToString("N0")),
                ("Displayed Sample Rows", summary.DisplayedCount.ToString("N0")),
                ("Displayed Result Type", summary.Sampled ? "Random sample of matching rows" : "All matching rows"),
                ("Download Scope", summary.Sampled ? "Summary + displayed sample rows + full breakdown" : "Summary + all matching rows + full breakdown"),
                ("Match Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            var wsMatches = wb.Worksheets.Add(sampleSheetName);
            StyleHeaderRow(wsMatches, 1, summary.Sampled ? "RULE 29 DISPLAYED RANDOM SAMPLE" : "RULE 29 MATCHING RECORDS", headers.Count);
            WriteRule29HeaderRow(wsMatches, 2, headers);
            WriteRule29Rows(wsMatches, 3, summary.MatchingRows, headers);

            var wsBreakdown = wb.Worksheets.Add("100% Breakdown");
            StyleHeaderRow(wsBreakdown, 1, "RULE 29 100% MATCHING BREAKDOWN", 2);
            wsBreakdown.Cell(2, 1).Value = string.IsNullOrWhiteSpace(summary.BreakdownColumn) ? "Breakdown Value" : summary.BreakdownColumn;
            wsBreakdown.Cell(2, 2).Value = "Count";
            for (var col = 1; col <= 2; col++)
            {
                var cell = wsBreakdown.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.Breakdown.Count; i++)
            {
                wsBreakdown.Cell(i + 3, 1).Value = summary.Breakdown[i].Value;
                wsBreakdown.Cell(i + 3, 2).Value = summary.Breakdown[i].Count;
            }
            wsBreakdown.Column(1).AdjustToContents();
            wsBreakdown.Column(2).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule21ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule21Headers(summary);

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 21: FIRST TIME ENTERING STUDENTS FILTERED VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("Source Table", summary.TableName),
                ("Filter Column", summary.FilterColumn),
                ("Filter Value", summary.FilterValue),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Filtered Records Tested", summary.TotalValidated.ToString("N0")),
                ("PASS Rows", summary.PassCount.ToString("N0")),
                ("FAIL Rows", summary.FailCount.ToString("N0")),
                ("PASS Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            var wsResults = wb.Worksheets.Add("Filtered Results");
            StyleHeaderRow(wsResults, 1, "RULE 21 FILTERED RESULTS", headers.Count);
            WriteRule21HeaderRow(wsResults, 2, headers);
            WriteRule21Rows(wsResults, 3, summary.MatchingRows, headers);

            var wsBreakdown = wb.Worksheets.Add("Distinct Values");
            StyleHeaderRow(wsBreakdown, 1, "RULE 21 DISTINCT VALUES", 2);
            wsBreakdown.Cell(2, 1).Value = summary.FilterColumn;
            wsBreakdown.Cell(2, 2).Value = "Count";
            for (var col = 1; col <= 2; col++)
            {
                var cell = wsBreakdown.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.Breakdown.Count; i++)
            {
                wsBreakdown.Cell(i + 3, 1).Value = summary.Breakdown[i].Value;
                wsBreakdown.Cell(i + 3, 2).Value = summary.Breakdown[i].Count;
            }
            wsBreakdown.Column(1).AdjustToContents();
            wsBreakdown.Column(2).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule17ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var converted = ToRule21Summary(summary);
            var headers = GetRule21Headers(converted);

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 17: GRADUATE STUDENTS FULFILLED QUALIFICATION VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("Source Table", summary.TableName),
                ("Filter Column", summary.FilterColumn),
                ("Filter Value", summary.FilterValue),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Filtered Records Tested", summary.TotalValidated.ToString("N0")),
                ("PASS Rows", summary.PassCount.ToString("N0")),
                ("FAIL Rows", summary.FailCount.ToString("N0")),
                ("PASS Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            var wsResults = wb.Worksheets.Add("Filtered Results");
            StyleHeaderRow(wsResults, 1, "RULE 17 FILTERED RESULTS", headers.Count);
            WriteRule21HeaderRow(wsResults, 2, headers);
            WriteRule21Rows(wsResults, 3, converted.MatchingRows, headers);

            var wsBreakdown = wb.Worksheets.Add("Distinct Values");
            StyleHeaderRow(wsBreakdown, 1, "RULE 17 DISTINCT VALUES", 2);
            wsBreakdown.Cell(2, 1).Value = summary.FilterColumn;
            wsBreakdown.Cell(2, 2).Value = "Count";
            for (var col = 1; col <= 2; col++)
            {
                var cell = wsBreakdown.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.Breakdown.Count; i++)
            {
                wsBreakdown.Cell(i + 3, 1).Value = summary.Breakdown[i].Value;
                wsBreakdown.Cell(i + 3, 2).Value = summary.Breakdown[i].Count;
            }
            wsBreakdown.Column(1).AdjustToContents();
            wsBreakdown.Column(2).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule16ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule16Headers();
            var includeDetailedControlSheets = summary.ReviewRows.Count <= Rule18DetailedSheetRowThreshold;
            var exportNote = includeDetailedControlSheets
                ? (summary.Warning ?? "")
                : "Large export mode: the workbook keeps the full All Results sheet and skips duplicate control tabs to reduce preparation time.";

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 16: STUDENT POPULATION VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("STUD Table", summary.StudTable),
                ("Bridge Table", summary.BridgeTable),
                ("CRSE Table", summary.CrseTable),
                ("Validation Date", summary.Timestamp),
                ("Join Path", summary.TableLinkageText),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Unfulfilled Qualification Population", summary.UnfulfilledPopulationCount.ToString("N0")),
                ("Control Result Rows", summary.TotalValidated.ToString("N0")),
                ("Matching Rows", summary.PassCount.ToString("N0")),
                ("Non-Matching Rows", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status),
                ("Export Note", exportNote)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 36;
            wsSummary.Column(2).Width = 70;

            var wsControlSummary = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsControlSummary, 1, "RULE 16 CONTROL SUMMARY", 6);
            var controlHeaders = new[] { "Control", "Criteria", "Matched Rows", "PASS", "FAIL", "Status" };
            for (var i = 0; i < controlHeaders.Length; i++)
            {
                var cell = wsControlSummary.Cell(2, i + 1);
                cell.Value = controlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            for (var i = 0; i < summary.ControlSummaries.Count; i++)
            {
                var item = summary.ControlSummaries[i];
                wsControlSummary.Cell(i + 3, 1).Value = item.ControlLabel;
                wsControlSummary.Cell(i + 3, 2).Value = item.CriteriaText;
                wsControlSummary.Cell(i + 3, 3).Value = item.TotalCount;
                wsControlSummary.Cell(i + 3, 4).Value = item.PassCount;
                wsControlSummary.Cell(i + 3, 5).Value = item.FailCount;
                wsControlSummary.Cell(i + 3, 6).Value = item.Status;
            }
            for (var c = 1; c <= 6; c++)
                wsControlSummary.Column(c).AdjustToContents();

            var wsAll = wb.Worksheets.Add("All Results");
            StyleHeaderRow(wsAll, 1, "RULE 16 ALL RESULTS", headers.Count);
            WriteRule16HeaderRow(wsAll, 2, headers);
            WriteRule16Rows(wsAll, 3, summary.ReviewRows, headers);

            if (includeDetailedControlSheets)
            {
                WriteRule16ControlSheet(wb, "Control1", "RULE 16 CONTROL 1", summary.ReviewRows.Where(r => string.Equals(r.ControlType, "Control_1", StringComparison.OrdinalIgnoreCase)).ToList(), headers);
                WriteRule16ControlSheet(wb, "Control2", "RULE 16 CONTROL 2", summary.ReviewRows.Where(r => string.Equals(r.ControlType, "Control_2", StringComparison.OrdinalIgnoreCase)).ToList(), headers);
                WriteRule16ControlSheet(wb, "Control3", "RULE 16 CONTROL 3", summary.ReviewRows.Where(r => string.Equals(r.ControlType, "Control_3", StringComparison.OrdinalIgnoreCase)).ToList(), headers);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule15ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule15Headers();
            var includeDetailedControlSheets = summary.ReviewRows.Count <= Rule18DetailedSheetRowThreshold;
            var exportNote = includeDetailedControlSheets
                ? (summary.Warning ?? "")
                : "Large export mode: the workbook keeps the full All Results sheet and skips duplicate control tabs to reduce preparation time.";

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 15: COURSE CREDENTIALS VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("QUAL Table", summary.QualTable),
                ("CRED Table", summary.CredTable),
                ("CREG Table", summary.RegistrationTable),
                ("Validation Date", summary.Timestamp),
                ("Join Path", summary.TableLinkageText),
                ("Validation Mode", summary.RuleModeText),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Approved Qualifications", summary.ApprovedQualificationCount.ToString("N0")),
                ("Approved Credential Rows", summary.ApprovedCredentialCount.ToString("N0")),
                ("Registered Credential Rows", summary.RegisteredCredentialCount.ToString("N0")),
                ("Missing Registrations", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status),
                ("Export Note", exportNote)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 36;
            wsSummary.Column(2).Width = 70;

            var wsControlSummary = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsControlSummary, 1, "RULE 15 CONTROL SUMMARY", 6);
            var controlHeaders = new[] { "Control", "Criteria", "Rows Tested", "PASS", "FAIL", "Status" };
            for (var i = 0; i < controlHeaders.Length; i++)
            {
                var cell = wsControlSummary.Cell(2, i + 1);
                cell.Value = controlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            for (var i = 0; i < summary.ControlSummaries.Count; i++)
            {
                var item = summary.ControlSummaries[i];
                wsControlSummary.Cell(i + 3, 1).Value = item.ControlLabel;
                wsControlSummary.Cell(i + 3, 2).Value = item.CriteriaText;
                wsControlSummary.Cell(i + 3, 3).Value = item.TotalCount;
                wsControlSummary.Cell(i + 3, 4).Value = item.PassCount;
                wsControlSummary.Cell(i + 3, 5).Value = item.FailCount;
                wsControlSummary.Cell(i + 3, 6).Value = item.Status;
            }
            for (var c = 1; c <= 6; c++)
                wsControlSummary.Column(c).AdjustToContents();

            var wsAll = wb.Worksheets.Add("All Results");
            StyleHeaderRow(wsAll, 1, "RULE 15 ALL RESULTS", headers.Count);
            WriteRule15HeaderRow(wsAll, 2, headers);
            WriteRule15Rows(wsAll, 3, summary.ReviewRows, headers);

            if (includeDetailedControlSheets)
            {
                WriteRule15ControlSheet(wb, "Control1", "RULE 15 CONTROL 1", summary.ReviewRows.Where(r => string.Equals(r.ControlType, "Control_1", StringComparison.OrdinalIgnoreCase)).ToList(), headers);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule14ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule14Headers();
            var includeDetailedControlSheets = summary.ReviewRows.Count <= Rule18DetailedSheetRowThreshold;
            var exportNote = includeDetailedControlSheets
                ? (summary.Warning ?? "")
                : "Large export mode: the workbook keeps the full All Results sheet and skips duplicate control tabs to reduce preparation time.";

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 14: COURSE REGISTRATION VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("CRSE Table", summary.StudTable),
                ("CREG Table", summary.BridgeTable),
                ("Validation Date", summary.Timestamp),
                ("Join Path", summary.TableLinkageText),
                ("Validation Mode", summary.RuleModeText),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Approved Courses", summary.ApprovedCourseCount.ToString("N0")),
                ("Registered Courses", summary.RegisteredCourseCount.ToString("N0")),
                ("Missing Registrations", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status),
                ("Export Note", exportNote)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 36;
            wsSummary.Column(2).Width = 70;

            var wsControlSummary = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsControlSummary, 1, "RULE 14 CONTROL SUMMARY", 6);
            var controlHeaders = new[] { "Control", "Criteria", "Rows Tested", "PASS", "FAIL", "Status" };
            for (var i = 0; i < controlHeaders.Length; i++)
            {
                var cell = wsControlSummary.Cell(2, i + 1);
                cell.Value = controlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            for (var i = 0; i < summary.ControlSummaries.Count; i++)
            {
                var item = summary.ControlSummaries[i];
                wsControlSummary.Cell(i + 3, 1).Value = item.ControlLabel;
                wsControlSummary.Cell(i + 3, 2).Value = item.CriteriaText;
                wsControlSummary.Cell(i + 3, 3).Value = item.TotalCount;
                wsControlSummary.Cell(i + 3, 4).Value = item.PassCount;
                wsControlSummary.Cell(i + 3, 5).Value = item.FailCount;
                wsControlSummary.Cell(i + 3, 6).Value = item.Status;
            }
            for (var c = 1; c <= 6; c++)
                wsControlSummary.Column(c).AdjustToContents();

            var wsAll = wb.Worksheets.Add("All Results");
            StyleHeaderRow(wsAll, 1, "RULE 14 ALL RESULTS", headers.Count);
            WriteRule14HeaderRow(wsAll, 2, headers);
            WriteRule14Rows(wsAll, 3, summary.ReviewRows, headers);

            if (includeDetailedControlSheets)
            {
                WriteRule14ControlSheet(wb, "Control1", "RULE 14 CONTROL 1", summary.ReviewRows.Where(r => string.Equals(r.ControlType, "Control_1", StringComparison.OrdinalIgnoreCase)).ToList(), headers);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule18ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule18Headers();
            var includeDetailedControlSheets = summary.ReviewRows.Count <= Rule18DetailedSheetRowThreshold;
            var exportNote = includeDetailedControlSheets
                ? (summary.Warning ?? "")
                : "Large export mode: the workbook keeps the full All Results sheet and skips duplicate control tabs to reduce preparation time.";

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 18: NSFAS STUDENT VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("STUD Table", summary.StudTable),
                ("Bridge Table", summary.BridgeTable),
                ("CRSE Table", summary.CrseTable),
                ("Validation Date", summary.Timestamp),
                ("Join Path", summary.TableLinkageText),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("NSFAS Population", summary.NsfasPopulationCount.ToString("N0")),
                ("Control Result Rows", summary.TotalValidated.ToString("N0")),
                ("Matching Rows", summary.PassCount.ToString("N0")),
                ("Non-Matching Rows", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status),
                ("Export Note", exportNote)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            var wsControlSummary = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsControlSummary, 1, "RULE 18 CONTROL SUMMARY", 6);
            var controlHeaders = new[] { "Control", "Criteria", "Matched Rows", "PASS", "FAIL", "Status" };
            for (var i = 0; i < controlHeaders.Length; i++)
            {
                var cell = wsControlSummary.Cell(2, i + 1);
                cell.Value = controlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            for (var i = 0; i < summary.ControlSummaries.Count; i++)
            {
                var item = summary.ControlSummaries[i];
                wsControlSummary.Cell(i + 3, 1).Value = item.ControlLabel;
                wsControlSummary.Cell(i + 3, 2).Value = item.CriteriaText;
                wsControlSummary.Cell(i + 3, 3).Value = item.TotalCount;
                wsControlSummary.Cell(i + 3, 4).Value = item.PassCount;
                wsControlSummary.Cell(i + 3, 5).Value = item.FailCount;
                wsControlSummary.Cell(i + 3, 6).Value = item.Status;
            }
            for (var c = 1; c <= 6; c++)
                wsControlSummary.Column(c).AdjustToContents();

            var wsAll = wb.Worksheets.Add("All Results");
            StyleHeaderRow(wsAll, 1, "RULE 18 ALL RESULTS", headers.Count);
            WriteRule18HeaderRow(wsAll, 2, headers);
            WriteRule18Rows(wsAll, 3, summary.ReviewRows, headers);

            if (includeDetailedControlSheets)
            {
                WriteRule18ControlSheet(wb, "Control1", "RULE 18 CONTROL 1", summary.ReviewRows.Where(r => string.Equals(r.ControlType, "Control_1", StringComparison.OrdinalIgnoreCase)).ToList(), headers);
                WriteRule18ControlSheet(wb, "Control2", "RULE 18 CONTROL 2", summary.ReviewRows.Where(r => string.Equals(r.ControlType, "Control_2", StringComparison.OrdinalIgnoreCase)).ToList(), headers);
                WriteRule18ControlSheet(wb, "Control3", "RULE 18 CONTROL 3", summary.ReviewRows.Where(r => string.Equals(r.ControlType, "Control_3", StringComparison.OrdinalIgnoreCase)).ToList(), headers);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule19ValidationSummary summary) =>
            ExportExcel(ToRule29Summary(summary));

        public byte[] ExportExcel(Rule25ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule25Headers();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 25: RECONCILE COURSE DATASETS", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("CRSE Table", summary.CrseTable),
                ("Audit Table", summary.AuditTable),
                ("H16 Table", summary.H16Table),
                ("CRSE Course Code Column", summary.CrseCourseCodeColumn),
                ("Audit Course Code Column", summary.AuditCourseCodeColumn),
                ("H16 Course Code Column", summary.H16CourseCodeColumn),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("CRSE Records", summary.CrseCount.ToString("N0")),
                ("Audit Records", summary.AuditCount.ToString("N0")),
                ("H16 Records", summary.H16Count.ToString("N0")),
                ("Total Reconciled Rows", summary.TotalValidated.ToString("N0")),
                ("Matched Rows", summary.Matches.ToString("N0")),
                ("Mismatch Rows", summary.Mismatches.ToString("N0")),
                ("Pass Sample Saved", summary.PassSampleCount.ToString("N0")),
                ("Mismatch Rate", $"{summary.ExceptionRate:F2}%"),
                ("Match Rate", $"{summary.MatchRate:F2}%"),
                ("Status", summary.Status),
                ("Warning", summary.Warning ?? "")
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 70;

            var wsPass = wb.Worksheets.Add("Pass Sample");
            StyleHeaderRow(wsPass, 1, "RULE 25 PASS SAMPLE", headers.Count);
            WriteRule25HeaderRow(wsPass, 2, headers);
            WriteRule25Rows(wsPass, 3, summary.PassSampleRows, headers, "#F3FFF3");

            var wsFail = wb.Worksheets.Add("Exceptions");
            StyleHeaderRow(wsFail, 1, "RULE 25 FAIL EXCEPTIONS", headers.Count);
            WriteRule25HeaderRow(wsFail, 2, headers);
            WriteRule25Rows(wsFail, 3, summary.FailRows, headers, "#FFF3F3");

            var wsBreakdown = wb.Worksheets.Add("Issue Breakdown");
            StyleHeaderRow(wsBreakdown, 1, "RULE 25 ISSUE BREAKDOWN", 2);
            wsBreakdown.Cell(2, 1).Value = "Status";
            wsBreakdown.Cell(2, 2).Value = "Count";
            for (var col = 1; col <= 2; col++)
            {
                var cell = wsBreakdown.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.IssueCounts.Count; i++)
            {
                wsBreakdown.Cell(i + 3, 1).Value = summary.IssueCounts[i].Status;
                wsBreakdown.Cell(i + 3, 2).Value = summary.IssueCounts[i].Count;
            }
            wsBreakdown.Column(1).AdjustToContents();
            wsBreakdown.Column(2).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule23ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule23Headers();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 23: RECONCILE DATASETS", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("STUD Table", summary.StudTable),
                ("Audit Table", summary.AuditTable),
                ("H16 Table", summary.H16Table),
                ("STUD Student # Column", summary.StudStudentNumberColumn),
                ("STUD Qualification Column", summary.StudQualificationColumn),
                ("STUD ID Column", summary.StudIdNumberColumn),
                ("Audit Student # Column", summary.AuditStudentNumberColumn),
                ("Audit Qualification Column", summary.AuditQualificationColumn),
                ("Audit ID Column", summary.AuditIdNumberColumn),
                ("H16 Student # Column", summary.H16StudentNumberColumn),
                ("H16 Qualification Column", summary.H16QualificationColumn),
                ("H16 ID Column", summary.H16IdNumberColumn),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("STUD Records", summary.StudCount.ToString("N0")),
                ("Audit Records", summary.AuditCount.ToString("N0")),
                ("H16 Records", summary.H16Count.ToString("N0")),
                ("Total Reconciled Rows", summary.TotalValidated.ToString("N0")),
                ("Matched Rows", summary.Matches.ToString("N0")),
                ("Mismatch Rows", summary.Mismatches.ToString("N0")),
                ("Pass Sample Saved", summary.PassSampleCount.ToString("N0")),
                ("Fail Rows Saved", summary.SavedFailRowCount.ToString("N0")),
                ("Mismatch Rate", $"{summary.ExceptionRate:F2}%"),
                ("Match Rate", $"{summary.MatchRate:F2}%"),
                ("Status", summary.Status),
                ("Warning", summary.Warning ?? "")
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 70;

            var wsPass = wb.Worksheets.Add("Pass Sample");
            StyleHeaderRow(wsPass, 1, "RULE 23 PASS SAMPLE", headers.Count);
            WriteRule23HeaderRow(wsPass, 2, headers);
            WriteRule23Rows(wsPass, 3, summary.PassSampleRows, headers, "#F3FFF3");

            var wsFail = wb.Worksheets.Add("Exceptions");
            StyleHeaderRow(wsFail, 1, "RULE 23 FAIL EXCEPTIONS", headers.Count);
            WriteRule23HeaderRow(wsFail, 2, headers);
            WriteRule23Rows(wsFail, 3, summary.FailRows, headers, "#FFF3F3");

            var wsBreakdown = wb.Worksheets.Add("Issue Breakdown");
            StyleHeaderRow(wsBreakdown, 1, "RULE 23 ISSUE BREAKDOWN", 2);
            wsBreakdown.Cell(2, 1).Value = "Status";
            wsBreakdown.Cell(2, 2).Value = "Count";
            for (var col = 1; col <= 2; col++)
            {
                var cell = wsBreakdown.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.IssueCounts.Count; i++)
            {
                wsBreakdown.Cell(i + 3, 1).Value = summary.IssueCounts[i].Status;
                wsBreakdown.Cell(i + 3, 2).Value = summary.IssueCounts[i].Count;
            }
            wsBreakdown.Column(1).AdjustToContents();
            wsBreakdown.Column(2).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule13ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 13: CESM QUALIFICATION POPULATION VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("CESM Table", summary.StudTable),
                ("QUAL Table", summary.QualTable),
                ("STUD Table", summary.CregTable),
                ("Linkage Used", summary.TableLinkageText),
                ("Overall PASS Rule", summary.OverallStatusRuleText),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Qualifying CESM Population", summary.FoundationStudentCount.ToString("N0")),
                ("Validated Qualifications", summary.TotalValidated.ToString("N0")),
                ("PASS Qualifications", summary.PassCount.ToString("N0")),
                ("FAIL Qualifications", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status),
                ("Warning", summary.Warning ?? "")
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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

            if (summary.ProcedureSteps.Any())
            {
                wsSummary.Cell(summaryRow, 1).Value = "PROCEDURE PERFORMED";
                wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                wsSummary.Cell(summaryRow, 1).Style.Font.FontColor = XLColor.White;
                wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                summaryRow++;

                for (var i = 0; i < summary.ProcedureSteps.Count; i++)
                {
                    wsSummary.Cell(summaryRow, 1).Value = $"Step {i + 1}";
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = summary.ProcedureSteps[i];
                    summaryRow++;
                }
            }

            wsSummary.Column(1).Width = 34;
            wsSummary.Column(2).Width = 80;

            var headers = new[]
            {
                "Validation Number",
                "Qualification Code (_001)",
                "CESM Major Field (_006)",
                "Qualification Description (_003)",
                "Step 1 CESM Check",
                "Linked STUD _001",
                "Step 2 Student Linkage",
                "Overall Result",
                "Explanation"
            };

            var wsRows = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsRows, 1, "RULE 13 VALIDATION RESULTS", headers.Length);
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = wsRows.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var rowIndex = 3;
            foreach (var row in summary.ReviewRows)
            {
                wsRows.Cell(rowIndex, 1).Value = row.ValidationNumber;
                wsRows.Cell(rowIndex, 2).Value = row.QualificationCode001;
                wsRows.Cell(rowIndex, 3).Value = row.CourseCode030;
                wsRows.Cell(rowIndex, 4).Value = row.QualificationDescription003;
                wsRows.Cell(rowIndex, 5).Value = row.FoundationFlag106;
                wsRows.Cell(rowIndex, 6).Value = row.FoundationCourse091;
                wsRows.Cell(rowIndex, 7).Value = row.NotebookStatus;
                wsRows.Cell(rowIndex, 8).Value = row.ValidationResult;
                wsRows.Cell(rowIndex, 9).Value = row.ValidationExplanation;

                wsRows.Range(rowIndex, 1, rowIndex, headers.Length).Style.Fill.BackgroundColor =
                    string.Equals(row.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase)
                        ? XLColor.FromHtml("#F3FFF3")
                        : XLColor.FromHtml("#FFF3F3");
                rowIndex++;
            }
            for (var c = 1; c <= headers.Length; c++) wsRows.Column(c).AdjustToContents();

            var failRows = summary.ReviewRows
                .Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (failRows.Any())
            {
                var wsFail = wb.Worksheets.Add("FAIL Rows");
                StyleHeaderRow(wsFail, 1, "RULE 13 FAIL ROWS", headers.Length);
                for (var i = 0; i < headers.Length; i++)
                {
                    var cell = wsFail.Cell(2, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    cell.Style.Font.FontColor = XLColor.White;
                }

                rowIndex = 3;
                foreach (var row in failRows)
                {
                    wsFail.Cell(rowIndex, 1).Value = row.ValidationNumber;
                    wsFail.Cell(rowIndex, 2).Value = row.QualificationCode001;
                    wsFail.Cell(rowIndex, 3).Value = row.CourseCode030;
                    wsFail.Cell(rowIndex, 4).Value = row.QualificationDescription003;
                    wsFail.Cell(rowIndex, 5).Value = row.FoundationFlag106;
                    wsFail.Cell(rowIndex, 6).Value = row.FoundationCourse091;
                    wsFail.Cell(rowIndex, 7).Value = row.NotebookStatus;
                    wsFail.Cell(rowIndex, 8).Value = row.ValidationResult;
                    wsFail.Cell(rowIndex, 9).Value = row.ValidationExplanation;
                    rowIndex++;
                }
                for (var c = 1; c <= headers.Length; c++) wsFail.Column(c).AdjustToContents();
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule12ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 12: COURSE SELECTION FROM dbo_CREG", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("CREG Table", summary.CregTable),
                ("CRSE Table", summary.CrseTable),
                ("Validation Date", summary.Timestamp),
                ("Join Path", summary.TableLinkageText),
                ("Validation Mode", summary.RuleModeText),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Selected Courses", summary.TotalValidated.ToString("N0")),
                ("Matched Courses", summary.PassCount.ToString("N0")),
                ("Missing Courses", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status),
                ("Export Note", summary.Warning ?? "")
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 34;
            wsSummary.Column(2).Width = 70;

            var wsControlSummary = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsControlSummary, 1, "RULE 12 CONTROL SUMMARY", 6);
            var controlHeaders = new[] { "Control", "Criteria", "Rows Tested", "PASS", "FAIL", "Status" };
            for (var i = 0; i < controlHeaders.Length; i++)
            {
                var cell = wsControlSummary.Cell(2, i + 1);
                cell.Value = controlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.ControlSummaries.Count; i++)
            {
                var item = summary.ControlSummaries[i];
                wsControlSummary.Cell(i + 3, 1).Value = item.ControlLabel;
                wsControlSummary.Cell(i + 3, 2).Value = item.CriteriaText;
                wsControlSummary.Cell(i + 3, 3).Value = item.TotalCount;
                wsControlSummary.Cell(i + 3, 4).Value = item.PassCount;
                wsControlSummary.Cell(i + 3, 5).Value = item.FailCount;
                wsControlSummary.Cell(i + 3, 6).Value = item.Status;
            }
            for (var c = 1; c <= 6; c++)
                wsControlSummary.Column(c).AdjustToContents();

            var wsAll = wb.Worksheets.Add("Dashboard Results");
            StyleHeaderRow(wsAll, 1, "RULE 12 DASHBOARD RESULTS", 8);

            var criteriaText = summary.ControlSummaries.FirstOrDefault()?.CriteriaText
                ?? "Active student qualification exists in QUAL.";

            wsAll.Range(2, 1, 2, 8).Merge();
            wsAll.Cell(2, 1).Value = $"CONTROL 1: {criteriaText}";
            wsAll.Cell(2, 1).Style.Font.Bold = true;
            wsAll.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#1F1F1F");
            wsAll.Cell(2, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
            wsAll.Cell(2, 1).Style.Alignment.WrapText = true;

            wsAll.Row(1).Height = 24;
            wsAll.Row(2).Height = 30;

            void StyleGroupHeader(int row, int startCol, int endCol, string title, string fillColor)
            {
                wsAll.Range(row, startCol, row, endCol).Merge();
                var cell = wsAll.Cell(row, startCol);
                cell.Value = title;
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(fillColor);
                cell.Style.Font.FontColor = XLColor.FromHtml("#1F1F1F");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            }

            StyleGroupHeader(4, 1, 3, summary.CregTable, "#E8C3E8");
            StyleGroupHeader(4, 4, 5, summary.QualTable, "#F8D1B5");
            StyleGroupHeader(4, 6, 7, summary.CresTable, "#FFFFFF");
            StyleGroupHeader(4, 8, 8, "Final Results", "#E7E2F5");

            var columnHeaders = new[]
            {
                summary.CregStudentCol,
                summary.CregQualCol,
                summary.CregCourseCol,
                summary.QualJoinCol,
                summary.QualDescCol,
                summary.CresCourseCol,
                summary.CresStatusCol,
                "pass/fail"
            };

            var headerFill = new[]
            {
                "#E8C3E8", "#E8C3E8", "#E8C3E8",
                "#F8D1B5", "#F8D1B5",
                "#FFFFFF", "#FFFFFF",
                "#E7E2F5"
            };

            for (var i = 0; i < columnHeaders.Length; i++)
            {
                var cell = wsAll.Cell(5, i + 1);
                cell.Value = columnHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(headerFill[i]);
                cell.Style.Font.FontColor = XLColor.FromHtml("#1F1F1F");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            }

            var rowIndex = 6;
            foreach (var row in summary.ReviewRows)
            {
                string Display(string key) => row.DisplayValues.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
                var finalMessage = Display("FINAL_RESULT_MESSAGE");
                var finalText = string.IsNullOrWhiteSpace(finalMessage)
                    ? row.ValidationExplanation
                    : finalMessage;
                var isFail = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase);
                var rowFill = isFail ? "#FFF3F3" : "#F3FFF3";

                wsAll.Cell(rowIndex, 1).Value = Display("CREG__007");
                wsAll.Cell(rowIndex, 2).Value = Display("CREG__001");
                wsAll.Cell(rowIndex, 3).Value = Display("CREG__030");
                wsAll.Cell(rowIndex, 4).Value = Display("QUAL__001");
                wsAll.Cell(rowIndex, 5).Value = Display("QUAL__003");
                wsAll.Cell(rowIndex, 6).Value = Display("CRES__030");
                wsAll.Cell(rowIndex, 7).Value = Display("CRES__031");
                wsAll.Cell(rowIndex, 8).Value = $"{row.ValidationResult}{Environment.NewLine}{finalText}";

                wsAll.Range(rowIndex, 1, rowIndex, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8C3E8");
                wsAll.Range(rowIndex, 4, rowIndex, 5).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8D1B5");
                wsAll.Range(rowIndex, 6, rowIndex, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFFFFF");
                wsAll.Cell(rowIndex, 8).Style.Fill.BackgroundColor = XLColor.FromHtml(rowFill);
                wsAll.Range(rowIndex, 1, rowIndex, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                wsAll.Range(rowIndex, 1, rowIndex, 8).Style.Alignment.WrapText = true;
                wsAll.Cell(rowIndex, 8).Style.Font.Bold = true;
                wsAll.Cell(rowIndex, 8).Style.Font.FontColor = isFail
                    ? XLColor.FromHtml("#B71C1C")
                    : XLColor.FromHtml("#1B5E20");

                rowIndex++;
            }

            wsAll.Columns(1, 8).AdjustToContents();
            wsAll.Column(5).Width = Math.Max(wsAll.Column(5).Width, 28);
            wsAll.Column(8).Width = Math.Max(wsAll.Column(8).Width, 32);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule10ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, BuildIntegrityExportTitle(summary), 2);
            var summaryData = BuildIntegritySummaryData(summary);

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
                }
                summaryRow++;
            }
            wsSummary.Column(1).Width = 34;
            wsSummary.Column(2).Width = 80;

            var wsRuleSummary = wb.Worksheets.Add("Rule Summary");
            StyleHeaderRow(wsRuleSummary, 1, $"{summary.RuleLabel.ToUpperInvariant()} SUMMARY", 7);
            var ruleHeaders = new[] { "Rule", "Name", "Table Scope", "Criteria", "Issues", "Severity", "Status" };
            for (var i = 0; i < ruleHeaders.Length; i++)
            {
                var cell = wsRuleSummary.Cell(2, i + 1);
                cell.Value = ruleHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.ControlSummaries.Count; i++)
            {
                var item = summary.ControlSummaries[i];
                wsRuleSummary.Cell(i + 3, 1).Value = item.RuleId;
                wsRuleSummary.Cell(i + 3, 2).Value = item.ControlLabel;
                wsRuleSummary.Cell(i + 3, 3).Value = item.TableName;
                wsRuleSummary.Cell(i + 3, 4).Value = item.CriteriaText;
                wsRuleSummary.Cell(i + 3, 5).Value = item.ErrorCount;
                wsRuleSummary.Cell(i + 3, 6).Value = item.Severity;
                wsRuleSummary.Cell(i + 3, 7).Value = item.Status;
            }
            for (var c = 1; c <= 7; c++) wsRuleSummary.Column(c).AdjustToContents();

            var detailKeys = summary.ReviewRows
                .SelectMany(row => row.DisplayValues.Keys)
                .Where(key => !string.Equals(key, "Validation_Result", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "Validation_Explanation", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "RULE_LABEL", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "RESULT_BADGE", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "FINAL_RESULT_MESSAGE", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var wsExceptions = wb.Worksheets.Add("Results");
            StyleHeaderRow(wsExceptions, 1, $"{BuildIntegrityExportTitle(summary)} - RESULTS", detailKeys.Count + 3);
            var headers = new List<string> { "Rule", "Validation Result", "Validation Explanation" };
            headers.AddRange(detailKeys.Select(key => GetIntegrityDetailHeader(summary, key)));
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = wsExceptions.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var rowIndex = 3;
            foreach (var row in summary.ReviewRows)
            {
                wsExceptions.Cell(rowIndex, 1).Value = row.RuleId;
                wsExceptions.Cell(rowIndex, 2).Value = row.ValidationResult;
                wsExceptions.Cell(rowIndex, 3).Value = row.ValidationExplanation;
                for (var i = 0; i < detailKeys.Count; i++)
                {
                    wsExceptions.Cell(rowIndex, i + 4).Value = row.DisplayValues.TryGetValue(detailKeys[i], out var value) ? value : "";
                }
                rowIndex++;
            }
            for (var c = 1; c <= headers.Count; c++) wsExceptions.Column(c).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static string BuildIntegrityExportTitle(Rule10ValidationSummary summary)
        {
            var ruleLabel = string.IsNullOrWhiteSpace(summary.RuleLabel) ? $"Rule {summary.RuleNumber}" : summary.RuleLabel.Trim();
            var ruleTitle = string.IsNullOrWhiteSpace(summary.RuleTitle) ? "Integrity Check" : summary.RuleTitle.Trim();
            return $"HEMIS {ruleLabel.ToUpperInvariant()}: {ruleTitle.ToUpperInvariant()}";
        }

        private static List<(string Label, string Value)> BuildIntegritySummaryData(Rule10ValidationSummary summary)
        {
            var rows = new List<(string Label, string Value)>
            {
                ("Database", summary.Database),
                ("Rule", summary.RuleLabel),
                ("Rule Title", summary.RuleTitle),
                ("Validation Date", summary.Timestamp),
                ("Validation Mode", summary.RuleModeText)
            };

            var criteria = summary.ControlSummaries.FirstOrDefault()?.CriteriaText;
            if (!string.IsNullOrWhiteSpace(criteria))
                rows.Add(("Criteria", criteria));

            var parameters = ParseIntegrityRuleParameters(summary.RuleParameterJson);

            switch (summary.RuleNumber)
            {
                case 1:
                case 2:
                    rows.Add(("QUAL Table", summary.QualTable));
                    rows.Add(("Check Column", summary.QualColumn));
                    break;
                case 3:
                    rows.Add(("QUAL Table", summary.QualTable));
                    rows.Add(("Duplicate Check Column", summary.QualColumn));
                    break;
                case 4:
                    rows.Add(("CRSE Table", summary.CrseTable));
                    rows.Add(("Duplicate Check Column", summary.CrseColumn));
                    break;
                case 5:
                case 6:
                    rows.Add(("STUD Table", summary.StudTable));
                    rows.Add(("Check Column", summary.StudColumn));
                    if (summary.RuleNumber == 5 && parameters.TryGetValue("MatchValue", out var matchValue) && !string.IsNullOrWhiteSpace(matchValue))
                        rows.Add(("Configured Invalid Value", matchValue));
                    break;
                case 7:
                    rows.Add(("STUD Table", summary.StudTable));
                    rows.Add(("QUAL Table", summary.QualTable));
                    rows.Add(("STUD Qualification Column", summary.StudColumn));
                    rows.Add(("QUAL Qualification Column", summary.QualColumn));
                    if (parameters.TryGetValue("ContextColumn", out var rule7Context) && !string.IsNullOrWhiteSpace(rule7Context))
                        rows.Add(("STUD Student Number Column", rule7Context));
                    break;
                case 8:
                    rows.Add(("CREG Table", summary.CregTable));
                    rows.Add(("CRSE Table", summary.CrseTable));
                    rows.Add(("CREG Course Column", summary.CregColumn));
                    rows.Add(("CRSE Course Column", summary.CrseColumn));
                    if (parameters.TryGetValue("ContextColumn", out var rule8Context) && !string.IsNullOrWhiteSpace(rule8Context))
                        rows.Add(("CREG Student Number Column", rule8Context));
                    break;
                case 9:
                    rows.Add(("CREG Table", summary.CregTable));
                    rows.Add(("STUD Table", summary.StudTable));
                    rows.Add(("CREG Student Number Column", summary.CregColumn));
                    rows.Add(("STUD Student Number Column", summary.StudColumn));
                    if (parameters.TryGetValue("ContextColumn", out var rule9CregCourse) && !string.IsNullOrWhiteSpace(rule9CregCourse))
                        rows.Add(("CREG Course Column", rule9CregCourse));
                    if (parameters.TryGetValue("SecondaryContextColumn", out var rule9StudCourse) && !string.IsNullOrWhiteSpace(rule9StudCourse))
                        rows.Add(("STUD Course Column", rule9StudCourse));
                    break;
                case 10:
                    rows.Add(("Join Verification", summary.TableLinkageText));
                    break;
                default:
                    rows.Add(("QUAL Table", summary.QualTable));
                    rows.Add(("STUD Table", summary.StudTable));
                    rows.Add(("CREG Table", summary.CregTable));
                    rows.Add(("CRSE Table", summary.CrseTable));
                    break;
            }

            rows.Add(("", ""));
            rows.Add(("RESULT SUMMARY", ""));
            rows.Add(("Total Checks", summary.TotalChecks.ToString("N0")));
            rows.Add(("Passed Checks", summary.PassedChecks.ToString("N0")));
            rows.Add(("Failed Checks", summary.FailedChecks.ToString("N0")));
            rows.Add(("Total Issues", summary.TotalIssues.ToString("N0")));
            rows.Add(("High Severity Rules", summary.HighSeverityCount.ToString("N0")));
            rows.Add(("Exception Rate", $"{summary.ExceptionRate:F2}%"));
            rows.Add(("Status", summary.Status));
            rows.Add(("Overall Status", summary.OverallStatusText));
            rows.Add(("Export Note", summary.Warning ?? ""));

            return rows;
        }

        private static Dictionary<string, string> ParseIntegrityRuleParameters(string? json)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json))
                return values;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return values;

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            values[property.Name] = value.Trim();
                    }
                }
            }
            catch
            {
            }

            return values;
        }

        private static string GetIntegrityDetailHeader(Rule10ValidationSummary summary, string key)
        {
            var normalized = key.ToUpperInvariant();
            return summary.RuleNumber switch
            {
                1 or 2 or 5 or 6 => normalized switch
                {
                    "TABLE_NAME" => "Selected Table",
                    "COLUMN_NAME" => "Check Column",
                    "COLUMN_VALUE" => "Exception Value",
                    "EXPECTED_VALUE" => "Configured Value",
                    _ => GetGenericIntegrityDetailHeader(key)
                },
                3 or 4 => normalized switch
                {
                    "TABLE_NAME" => "Selected Table",
                    "COLUMN_NAME" => "Duplicate Check Column",
                    "DUPLICATE_VALUE" => "Duplicated Value",
                    _ => GetGenericIntegrityDetailHeader(key)
                },
                7 => normalized switch
                {
                    "STUDENT_TABLE_NAME" => "STUD Table",
                    "STUDENT_COLUMN_NAME" => "Student Number Column",
                    "STUDENT_COLUMN_VALUE" => "Student Number",
                    "LEFT_TABLE_NAME" => "STUD Table",
                    "LEFT_COLUMN_NAME" => "STUD Qualification Column",
                    "LEFT_COLUMN_VALUE" => "STUD Qualification Value",
                    "RIGHT_TABLE_NAME" => "QUAL Table",
                    "RIGHT_COLUMN_NAME" => "QUAL Qualification Column",
                    "RIGHT_COLUMN_VALUE" => "QUAL Qualification Value",
                    _ => GetGenericIntegrityDetailHeader(key)
                },
                8 => normalized switch
                {
                    "STUDENT_COLUMN_VALUE" => "Student Number",
                    "LEFT_TABLE_NAME" => "CREG Table",
                    "LEFT_COLUMN_NAME" => "CREG Course Column",
                    "LEFT_COLUMN_VALUE" => "CREG Course Value",
                    "RIGHT_TABLE_NAME" => "CRSE Table",
                    "RIGHT_COLUMN_NAME" => "CRSE Course Column",
                    "RIGHT_COLUMN_VALUE" => "CRSE Course Value",
                    _ => GetGenericIntegrityDetailHeader(key)
                },
                9 => normalized switch
                {
                    "LEFT_STUDENT_COLUMN_NAME" => "CREG Student Number Column",
                    "LEFT_STUDENT_COLUMN_VALUE" => "CREG Student Number",
                    "RIGHT_STUDENT_COLUMN_NAME" => "STUD Student Number Column",
                    "RIGHT_STUDENT_COLUMN_VALUE" => "STUD Student Number",
                    "LEFT_CONTEXT_TABLE_NAME" => "CREG Table",
                    "LEFT_COLUMN_NAME" => "CREG Course Column",
                    "LEFT_COLUMN_VALUE" => "CREG Course Value",
                    "RIGHT_CONTEXT_TABLE_NAME" => "STUD Table",
                    "RIGHT_COLUMN_NAME" => "STUD Course Column",
                    "RIGHT_COLUMN_VALUE" => "STUD Course Value",
                    _ => GetGenericIntegrityDetailHeader(key)
                },
                10 => normalized switch
                {
                    "TABLE_NAME" => "Dataset Table",
                    "COLUMN_NAME" => "Key Column",
                    "COLUMN_VALUE" => "Detected Value",
                    _ => GetGenericIntegrityDetailHeader(key)
                },
                _ => GetGenericIntegrityDetailHeader(key)
            };
        }

        private static string GetGenericIntegrityDetailHeader(string key) =>
            key.ToUpperInvariant() switch
            {
                "TABLE_NAME" => "Table",
                "COLUMN_NAME" => "Column",
                "COLUMN_VALUE" => "Column Value",
                "EXPECTED_VALUE" => "Configured Value",
                "DUPLICATE_VALUE" => "Duplicate Value",
                "DUPLICATE_COUNT" => "Duplicate Count",
                "STUDENT_TABLE_NAME" => "Student Table",
                "STUDENT_COLUMN_NAME" => "Student Column",
                "STUDENT_COLUMN_VALUE" => "Student Value",
                "LEFT_TABLE_NAME" => "Left Table",
                "LEFT_COLUMN_NAME" => "Left Column",
                "LEFT_COLUMN_VALUE" => "Left Value",
                "RIGHT_TABLE_NAME" => "Right Table",
                "RIGHT_COLUMN_NAME" => "Right Column",
                "RIGHT_COLUMN_VALUE" => "Right Value",
                "LEFT_STUDENT_COLUMN_NAME" => "Left Student Column",
                "LEFT_STUDENT_COLUMN_VALUE" => "Left Student Value",
                "RIGHT_STUDENT_COLUMN_NAME" => "Right Student Column",
                "RIGHT_STUDENT_COLUMN_VALUE" => "Right Student Value",
                "LEFT_CONTEXT_TABLE_NAME" => "Left Context Table",
                "RIGHT_CONTEXT_TABLE_NAME" => "Right Context Table",
                _ => key.Replace('_', ' ')
            };

        public byte[] ExportExcel(Rule11ValidationSummary summary)
            => ExportRule11Excel(summary);

        public byte[] ExportExcel(Rule20ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var headers = GetRule20DashboardHeaders();
            var previewRows = BuildRule20DashboardPreviewRows(summary.ReviewRows);

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 20: FOUNDATION VALIDATION (3-PART)", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("STUD Table", summary.StudTable),
                ("QUAL Table", summary.QualTable),
                ("Bridge Table", summary.CregTable),
                ("CRSE Table", summary.CrseTable),
                ("Linkage Used", summary.TableLinkageText),
                ("Overall PASS Rule", summary.OverallStatusRuleText),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Foundation Students", summary.FoundationStudentCount.ToString("N0")),
                ("Total Validated Rows", summary.TotalValidated.ToString("N0")),
                ("PASS Rows", summary.PassCount.ToString("N0")),
                ("FAIL Rows", summary.FailCount.ToString("N0")),
                ("Fail Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status),
                ("Warning", summary.Warning ?? "")
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
            if (summary.ProcedureSteps.Any())
            {
                wsSummary.Cell(summaryRow, 1).Value = "PROCEDURE PERFORMED";
                wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                wsSummary.Cell(summaryRow, 1).Style.Font.FontColor = XLColor.White;
                wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                summaryRow++;

                for (var i = 0; i < summary.ProcedureSteps.Count; i++)
                {
                    wsSummary.Cell(summaryRow, 1).Value = $"Step {i + 1}";
                    wsSummary.Cell(summaryRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(summaryRow, 2).Value = summary.ProcedureSteps[i];
                    summaryRow++;
                }
            }
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 72;

            var wsRows = wb.Worksheets.Add("Validated Rows");
            StyleHeaderRow(wsRows, 1, "RULE 20 VALIDATED ROWS", headers.Count);
            WriteRule20DashboardHeaderRow(wsRows, 2, headers);
            WriteRule20DashboardRows(wsRows, 3, previewRows, headers);

            var failRows = previewRows
                .Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (failRows.Any())
            {
                var wsFail = wb.Worksheets.Add("FAIL Rows");
                StyleHeaderRow(wsFail, 1, "RULE 20 FAIL ROWS", headers.Count);
                WriteRule20DashboardHeaderRow(wsFail, 2, headers);
                WriteRule20DashboardRows(wsFail, 3, failRows, headers);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule22ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var headers = GetRule22Headers();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 22: STAFF SAMPLING (DBO_PROF)", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("PROF Table", summary.ProfTable),
                ("Column _041", summary.Column041),
                ("Column _039", summary.Column039),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("Total Validated", summary.TotalValidated.ToString("N0")),
                ("PASS Rows", summary.PassCount.ToString("N0")),
                ("FAIL Rows", summary.FailCount.ToString("N0")),
                ("Fail Rate", $"{summary.ExceptionRate:F2}%"),
                ("Control 1 Available", summary.Control1Count.ToString("N0")),
                ("Control 2 Available", summary.Control2Count.ToString("N0")),
                ("Control 3 Available", summary.Control3Count.ToString("N0")),
                ("Status", summary.Status),
                ("Warning", summary.Warning ?? "")
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 72;

            var wsControls = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsControls, 1, "RULE 22 CONTROL SUMMARY", 5);
            var controlHeaders = new[] { "Control", "Definition", "Rows", "PASS", "FAIL" };
            for (var i = 0; i < controlHeaders.Length; i++)
            {
                var cell = wsControls.Cell(2, i + 1);
                cell.Value = controlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            for (var i = 0; i < summary.ControlSummaries.Count; i++)
            {
                var item = summary.ControlSummaries[i];
                wsControls.Cell(i + 3, 1).Value = item.ControlType;
                wsControls.Cell(i + 3, 2).Value = item.ControlDefinition;
                wsControls.Cell(i + 3, 3).Value = item.AvailableCount;
                wsControls.Cell(i + 3, 4).Value = item.PassCount;
                wsControls.Cell(i + 3, 5).Value = item.FailCount;
            }
            for (var c = 1; c <= controlHeaders.Length; c++) wsControls.Column(c).AdjustToContents();

            var wsRows = wb.Worksheets.Add("Review Rows");
            StyleHeaderRow(wsRows, 1, "RULE 22 REVIEW ROWS", headers.Count);
            WriteRule22HeaderRow(wsRows, 2, headers);
            WriteRule22Rows(wsRows, 3, summary.ReviewRows, headers);

            var failRows = summary.ReviewRows
                .Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (failRows.Any())
            {
                var wsFail = wb.Worksheets.Add("FAIL Rows");
                StyleHeaderRow(wsFail, 1, "RULE 22 FAIL ROWS", headers.Count);
                WriteRule22HeaderRow(wsFail, 2, headers);
                WriteRule22Rows(wsFail, 3, failRows, headers);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule24ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = GetRule24Headers();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 24: RECONCILE QUALIFICATION DATASETS", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("QUAL Table", summary.QualTable),
                ("Audit Table", summary.AuditTable),
                ("H16 Table", summary.H16Table),
                ("QUAL Code Column", summary.QualCodeColumn),
                ("Approval Status Column", summary.Control1OnlyMode ? "Not applied" : summary.ApprovalStatusColumn),
                ("Excluded Approval Value", summary.Control1OnlyMode ? "Not applied" : summary.ExcludedApprovalStatusValue),
                ("Validation Mode", summary.Control1OnlyMode ? "Control 1 Only" : "Control 1 + Control 2"),
                ("Audit Qualification Code Column", summary.AuditQualCodeColumn),
                ("H16 Qualification Code Column", summary.H16QualCodeColumn),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("QUAL Records", summary.QualCount.ToString("N0")),
                ("Audit Records", summary.AuditCount.ToString("N0")),
                ("H16 Records", summary.H16Count.ToString("N0")),
                ("Total Reconciled Rows", summary.TotalValidated.ToString("N0")),
                ("Matched Rows", summary.Matches.ToString("N0")),
                ("Mismatch Rows", summary.Mismatches.ToString("N0")),
                ("Pass Sample Saved", summary.PassSampleCount.ToString("N0")),
                ("Mismatch Rate", $"{summary.ExceptionRate:F2}%"),
                ("Match Rate", $"{summary.MatchRate:F2}%"),
                ("Status", summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 70;

            var wsPass = wb.Worksheets.Add("Pass Sample");
            StyleHeaderRow(wsPass, 1, "RULE 24 PASS SAMPLE", headers.Count);
            WriteRule24HeaderRow(wsPass, 2, headers);
            WriteRule24Rows(wsPass, 3, summary.PassSampleRows, headers, "#F3FFF3");

            var wsFail = wb.Worksheets.Add("Exceptions");
            StyleHeaderRow(wsFail, 1, "RULE 24 FAIL EXCEPTIONS", headers.Count);
            WriteRule24HeaderRow(wsFail, 2, headers);
            WriteRule24Rows(wsFail, 3, summary.FailRows, headers, "#FFF3F3");

            var wsBreakdown = wb.Worksheets.Add("Issue Breakdown");
            StyleHeaderRow(wsBreakdown, 1, "RULE 24 ISSUE BREAKDOWN", 2);
            wsBreakdown.Cell(2, 1).Value = "Status";
            wsBreakdown.Cell(2, 2).Value = "Count";
            for (var col = 1; col <= 2; col++)
            {
                var cell = wsBreakdown.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            for (var i = 0; i < summary.IssueCounts.Count; i++)
            {
                wsBreakdown.Cell(i + 3, 1).Value = summary.IssueCounts[i].Status;
                wsBreakdown.Cell(i + 3, 2).Value = summary.IssueCounts[i].Count;
            }
            wsBreakdown.Column(1).AdjustToContents();
            wsBreakdown.Column(2).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportExcel(Rule27ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = new[] { "Validation_Number", "Filter_Value" }
                .Concat(summary.MatchingRows
                    .SelectMany(r => r.DisplayValues.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 27: ERROR VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("Source Table", summary.TableName),
                ("Filter Column", summary.FilterColumn),
                ("Filter Value", summary.FilterValue),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("100% Source Records Tested", summary.TotalValidated.ToString("N0")),
                ("Matching Records Retrieved", summary.MatchingCount.ToString("N0")),
                ("Coverage", "100%"),
                ("Match Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };

            var summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "RESULT SUMMARY")
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
                }

                summaryRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            var wsMatches = wb.Worksheets.Add("Filtered Records");
            StyleHeaderRow(wsMatches, 1, "RULE 27 FILTERED RECORDS", headers.Count);
            WriteRule27HeaderRow(wsMatches, 2, headers);
            WriteRule27Rows(wsMatches, 3, summary.MatchingRows, headers);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportCsv(Rule26ValidationSummary summary)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Direction,ControlNumber,ControlName,PersonnelNumber,PersonnelName,ExceptionReason,BaseValue,ReferenceValue");
            foreach (var row in summary.Exceptions)
            {
                builder
                    .Append(CsvEscape(row.DirectionLabel)).Append(',')
                    .Append(row.ControlNumber).Append(',')
                    .Append(CsvEscape(row.ControlName)).Append(',')
                    .Append(CsvEscape(row.PersonnelNumber)).Append(',')
                    .Append(CsvEscape(row.PersonnelName ?? "")).Append(',')
                    .Append(CsvEscape(row.ExceptionReason)).Append(',')
                    .Append(CsvEscape(row.BaseValue)).Append(',')
                    .Append(CsvEscape(row.ReferenceValue)).AppendLine();
            }

            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        public byte[] ExportExcel(Rule32ValidationSummary summary)
            => ExportFatalErrorExcel(summary, 32, "STUD");

        public byte[] ExportExcel(Rule31ValidationSummary summary) =>
            ExportFatalErrorExcel(ToRule32Summary(summary), 31, "QUAL");

        private byte[] ExportFatalErrorExcel(Rule32ValidationSummary summary, int ruleNumber, string tableScope)
        {
            using var wb = new XLWorkbook();

            var allHeaders = GetRule32Headers(summary);

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, $"HEMIS RULE {ruleNumber}: FATAL ERRORS WITH EXCLUSIONS ({tableScope})", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("Source Table", summary.TableName),
                ("Error Type Column", summary.ErrorTypeColumn),
                ("Error Column", summary.ErrorColumn),
                ("Filter Value", summary.ErrorTypeValue),
                ("Exclusion Codes", string.Join(", ", summary.Exclusions)),
                ("Normalized Codes", string.Join(", ", summary.NormalizedExclusions)),
                ("Validation Date", summary.Timestamp),
                ("", ""),
                ("VALIDATION RESULTS", ""),
                ("Total Fatal Errors", summary.TotalFatal.ToString("N0")),
                ("Excluded", summary.ExcludedCount.ToString("N0")),
                ("Remaining", summary.RemainingCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
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
            wsSummary.Column(2).Width = 70;

            var wsExcluded = wb.Worksheets.Add("Excluded");
            StyleHeaderRow(wsExcluded, 1, $"RULE {ruleNumber} EXCLUDED FATAL ERRORS", allHeaders.Count);
            WriteRule32HeaderRow(wsExcluded, 2, allHeaders);
            WriteRule32Rows(wsExcluded, 3, summary.ExcludedRows, allHeaders, "#F3FFF3");

            var wsRemaining = wb.Worksheets.Add("Remaining");
            StyleHeaderRow(wsRemaining, 1, $"RULE {ruleNumber} REMAINING FATAL ERRORS", allHeaders.Count);
            WriteRule32HeaderRow(wsRemaining, 2, allHeaders);
            WriteRule32Rows(wsRemaining, 3, summary.RemainingRows, allHeaders, "#FFF3F3");

            var wsStats = wb.Worksheets.Add("Breakdown");
            StyleHeaderRow(wsStats, 1, $"RULE {ruleNumber} ERROR CODE BREAKDOWN", 4);
            wsStats.Cell(2, 1).Value = "Excluded Code";
            wsStats.Cell(2, 2).Value = "Excluded Count";
            wsStats.Cell(2, 3).Value = "Remaining Code";
            wsStats.Cell(2, 4).Value = "Remaining Count";
            for (var col = 1; col <= 4; col++)
            {
                var cell = wsStats.Cell(2, col);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var maxRows = Math.Max(summary.ExcludedBreakdown.Count, summary.RemainingBreakdown.Count);
            for (var i = 0; i < maxRows; i++)
            {
                if (i < summary.ExcludedBreakdown.Count)
                {
                    wsStats.Cell(i + 3, 1).Value = summary.ExcludedBreakdown[i].ErrorCode;
                    wsStats.Cell(i + 3, 2).Value = summary.ExcludedBreakdown[i].Count;
                }
                if (i < summary.RemainingBreakdown.Count)
                {
                    wsStats.Cell(i + 3, 3).Value = summary.RemainingBreakdown[i].ErrorCode;
                    wsStats.Cell(i + 3, 4).Value = summary.RemainingBreakdown[i].Count;
                }
            }
            for (var col = 1; col <= 4; col++) wsStats.Column(col).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // ─── CSV Export ───────────────────────────────────────────────────────
        public byte[] ExportRule36Csv(ValidationSummary summary, bool exceptionsOnly = false)
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

        public byte[] ExportRule37Excel(Rule37ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            // Sheet 1: Full Validation Results
            var wsResults = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsResults, 1, "RULE 37 — CESM vs PQM VALIDATION", 9);
            var headers = new[]
            {
                "#", "Record ID", "HEMIS CESM Code", "HEMIS Qual Name",
                "PQM Code", "PQM Name", "Code Match", "Name Match", "Result"
            };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsResults.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            int rowIndex = 3;
            foreach (var row in summary.ValidationRows)
            {
                wsResults.Cell(rowIndex, 1).Value = row.ValidationNumber;
                wsResults.Cell(rowIndex, 2).Value = row.RecordId;
                wsResults.Cell(rowIndex, 3).Value = row.HemisCesmCode;
                wsResults.Cell(rowIndex, 4).Value = row.HemisQualName;
                wsResults.Cell(rowIndex, 5).Value = row.PqmCode ?? "";
                wsResults.Cell(rowIndex, 6).Value = row.PqmName ?? "";
                wsResults.Cell(rowIndex, 7).Value = row.CodeMatch ? "YES" : "NO";
                wsResults.Cell(rowIndex, 8).Value = row.NameMatch ? "YES" : "NO";
                wsResults.Cell(rowIndex, 9).Value = row.ValidationResult;
                wsResults.Range(rowIndex, 1, rowIndex, 9).Style.Fill.BackgroundColor =
                    row.ValidationResult == "PASS" ? XLColor.FromHtml("#F3FFF3") : XLColor.FromHtml("#FFF3F3");
                wsResults.Cell(rowIndex, 9).Style.Font.Bold = true;
                wsResults.Cell(rowIndex, 9).Style.Font.FontColor =
                    row.ValidationResult == "PASS" ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");
                rowIndex++;
            }
            for (int c = 1; c <= 9; c++) wsResults.Column(c).AdjustToContents();

            // Sheet 2: Exceptions
            var wsEx = wb.Worksheets.Add("Exceptions");
            StyleHeaderRow(wsEx, 1, "RULE 37 EXCEPTIONS — CESM vs PQM", 9);
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsEx.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            int exRow = 3;
            foreach (var ex in summary.Exceptions)
            {
                wsEx.Cell(exRow, 1).Value = ex.ValidationNumber;
                wsEx.Cell(exRow, 2).Value = ex.RecordId;
                wsEx.Cell(exRow, 3).Value = ex.HemisCesmCode;
                wsEx.Cell(exRow, 4).Value = ex.HemisQualName;
                wsEx.Cell(exRow, 5).Value = ex.PqmCode ?? "";
                wsEx.Cell(exRow, 6).Value = ex.PqmName ?? "";
                wsEx.Cell(exRow, 7).Value = ex.CodeMatch ? "YES" : "NO";
                wsEx.Cell(exRow, 8).Value = ex.NameMatch ? "YES" : "NO";
                wsEx.Cell(exRow, 9).Value = ex.ValidationResult;
                wsEx.Range(exRow, 1, exRow, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3F3");
                wsEx.Cell(exRow, 9).Style.Font.FontColor = XLColor.FromHtml("#C62828");
                wsEx.Cell(exRow, 9).Style.Font.Bold = true;
                exRow++;
            }
            for (int c = 1; c <= 9; c++) wsEx.Column(c).AdjustToContents();

            // Add an "Exception Reason" column for exceptions sheet
            wsEx.Cell(2, 10).Value = "Exception Reason";
            wsEx.Cell(2, 10).Style.Font.Bold = true;
            wsEx.Cell(2, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
            wsEx.Cell(2, 10).Style.Font.FontColor = XLColor.White;
            int exReasonRow = 3;
            foreach (var ex in summary.Exceptions)
            {
                wsEx.Cell(exReasonRow, 10).Value = ex.ExceptionReason;
                exReasonRow++;
            }
            wsEx.Column(10).AdjustToContents();

            // Sheet 3: Summary
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 37: CESM vs PQM VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database",            summary.Database),
                ("CESM Table",          $"{summary.CesmTable} (ID: {summary.CesmIdCol}, Code: {summary.CesmCodeCol})"),
                ("QUAL Table",          $"{summary.QualTable} (ID: {summary.QualIdCol}, Name: {summary.QualNameCol})"),
                ("PQM Table",           $"{summary.PqmTable} (Name: {summary.PqmNameCol}, Code1: {summary.PqmCode1Col}, Code2: {summary.PqmCode2Col})"),
                ("Validation Date",     summary.Timestamp),
                ("",                    ""),
                ("VALIDATION RESULTS",  ""),
                ("Total Validated",     summary.TotalValidated.ToString("N0")),
                ("PASS (Matched)",      summary.PassCount.ToString("N0")),
                ("FAIL (Mismatch)",     summary.FailCount.ToString("N0")),
                ("Exception Rate",      $"{summary.ExceptionRate:F2}%"),
                ("Status",              summary.Status)
            };

            int summaryRow = 2;
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
            wsSummary.Column(2).Width = 70;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule37Csv(Rule37ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();

            if (exceptionsOnly)
            {
                sb.AppendLine("Validation_Number,Record_ID,HEMIS_CESM_Code,HEMIS_Qual_Name,PQM_Code,PQM_Name,Code_Match,Name_Match,Validation_Result,Exception_Reason");
                foreach (var ex in summary.Exceptions)
                {
                    sb.AppendLine(string.Join(",",
                        ex.ValidationNumber,
                        CsvEscape(ex.RecordId),
                        CsvEscape(ex.HemisCesmCode),
                        CsvEscape(ex.HemisQualName),
                        CsvEscape(ex.PqmCode),
                        CsvEscape(ex.PqmName),
                        ex.CodeMatch ? "YES" : "NO",
                        ex.NameMatch ? "YES" : "NO",
                        ex.ValidationResult,
                        CsvEscape(ex.ExceptionReason)));
                }
            }
            else
            {
                sb.AppendLine("Validation_Number,Record_ID,HEMIS_CESM_Code,HEMIS_Qual_Name,PQM_Code,PQM_Name,Code_Match,Name_Match,Validation_Result,Exception_Reason");
                foreach (var row in summary.ValidationRows)
                {
                    sb.AppendLine(string.Join(",",
                        row.ValidationNumber,
                        CsvEscape(row.RecordId),
                        CsvEscape(row.HemisCesmCode),
                        CsvEscape(row.HemisQualName),
                        CsvEscape(row.PqmCode),
                        CsvEscape(row.PqmName),
                        row.CodeMatch ? "YES" : "NO",
                        row.NameMatch ? "YES" : "NO",
                        row.ValidationResult,
                        CsvEscape(row.ExceptionReason)));
                }
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportRule38Excel(Rule38ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = new[]
            {
                "#", "QUAL Code", "QUAL Name", "Approval",
                "QUAL Type", "QUAL Min Time", "QUAL WIL", "QUAL HEQF", "QUAL Subsidy",
                "PQM Match", "PQM Type", "PQM Min Time", "PQM WIL", "PQM Accreditation", "PQM Subsidy",
                "C2 Type", "C3 Time", "C4 WIL", "C5 HEQF", "C5 Expected", "C6 Subsidy",
                "Failed Controls", "Result"
            };
            const int colCount = 23;

            // Sheet 1: All results
            var wsAll = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsAll, 1, "RULE 38 — ENHANCED QUAL vs PQM VALIDATION", colCount);
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsAll.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            int rowIdx = 3;
            foreach (var row in summary.ValidationRows)
            {
                WriteRule38ExcelRow(wsAll, rowIdx++, row);
            }
            for (int c = 1; c <= colCount; c++) wsAll.Column(c).AdjustToContents();

            // Sheet 2: Exceptions only
            var wsEx = wb.Worksheets.Add("Exceptions");
            StyleHeaderRow(wsEx, 1, "RULE 38 EXCEPTIONS — QUAL vs PQM", colCount);
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsEx.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            int exIdx = 3;
            foreach (var row in summary.ValidationRows.Where(r => r.ValidationResult == "FAIL"))
            {
                WriteRule38ExcelRow(wsEx, exIdx++, row);
            }
            for (int c = 1; c <= colCount; c++) wsEx.Column(c).AdjustToContents();

            // Sheet 3: Control summary
            var wsCtrl = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsCtrl, 1, "RULE 38 — CONTROL SUMMARY", 5);
            var ctrlHeaders = new[] { "Control", "Description", "Criteria", "Pass", "Fail" };
            for (int i = 0; i < ctrlHeaders.Length; i++)
            {
                var cell = wsCtrl.Cell(2, i + 1);
                cell.Value = ctrlHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            int ctrlRow = 3;
            foreach (var cs in summary.ControlSummaries)
            {
                wsCtrl.Cell(ctrlRow, 1).Value = cs.ControlId;
                wsCtrl.Cell(ctrlRow, 2).Value = cs.ControlLabel;
                wsCtrl.Cell(ctrlRow, 3).Value = cs.CriteriaText;
                wsCtrl.Cell(ctrlRow, 4).Value = cs.PassCount;
                wsCtrl.Cell(ctrlRow, 5).Value = cs.FailCount;
                ctrlRow++;
            }
            wsCtrl.Column(1).Width = 12;
            wsCtrl.Column(2).Width = 30;
            wsCtrl.Column(3).Width = 50;
            wsCtrl.Column(4).Width = 10;
            wsCtrl.Column(5).Width = 10;

            // Sheet 4: Summary stats
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 38: ENHANCED QUAL vs PQM VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database",          summary.Database),
                ("QUAL Table",        $"{summary.QualTable} (ID: {summary.QualIdCol}, Name: {summary.QualNameCol})"),
                ("PQM Table",         $"{summary.PqmTable} (Name: {summary.PqmNameCol})"),
                ("Validation Date",   summary.Timestamp),
                ("",                  ""),
                ("VALIDATION RESULTS",""),
                ("Total QUAL Records",summary.TotalQualRecords.ToString("N0")),
                ("Approved Count",    summary.ApprovedCount.ToString("N0")),
                ("PQM Match Count",   summary.PqmMatchCount.ToString("N0")),
                ("PQM No Match",      summary.PqmNoMatchCount.ToString("N0")),
                ("Overall PASS",      summary.OverallPassCount.ToString("N0")),
                ("Overall FAIL",      summary.OverallFailCount.ToString("N0")),
                ("Exception Rate",    $"{summary.ExceptionRate:F2}%"),
                ("Status",            summary.Status)
            };
            int sumRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                {
                    var hdrCell = wsSummary.Cell(sumRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(sumRow, 1, sumRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(sumRow, 1).Value = label;
                    wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(sumRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sumRow, 2).Value = value;
                    if (label == "Status")
                    {
                        wsSummary.Cell(sumRow, 2).Style.Fill.BackgroundColor =
                            value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(sumRow, 2).Style.Font.Bold = true;
                    }
                }
                sumRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void WriteRule38ExcelRow(IXLWorksheet ws, int rowIdx, Rule38ValidationRow row)
        {
            ws.Cell(rowIdx, 1).Value  = row.ValidationNumber;
            ws.Cell(rowIdx, 2).Value  = row.QualCode;
            ws.Cell(rowIdx, 3).Value  = row.QualName;
            ws.Cell(rowIdx, 4).Value  = row.ApprovalStatus;
            ws.Cell(rowIdx, 5).Value  = row.QualType ?? "";
            ws.Cell(rowIdx, 6).Value  = row.MinTimeTotal ?? "";
            ws.Cell(rowIdx, 7).Value  = row.MinTimeWIL ?? "";
            ws.Cell(rowIdx, 8).Value  = row.HeqfIndicator ?? "";
            ws.Cell(rowIdx, 9).Value  = row.TotalSubsidy ?? "";
            ws.Cell(rowIdx, 10).Value = row.HasPqmMatch ? "YES" : "NO";
            ws.Cell(rowIdx, 11).Value = row.PqmQualType ?? "";
            ws.Cell(rowIdx, 12).Value = row.PqmMinTimeTotal ?? "";
            ws.Cell(rowIdx, 13).Value = row.PqmWIL ?? "";
            ws.Cell(rowIdx, 14).Value = row.PqmAccreditation ?? "";
            ws.Cell(rowIdx, 15).Value = row.PqmTotalSubsidy ?? "";
            ws.Cell(rowIdx, 16).Value = row.C2_TypeMatch ? "PASS" : "FAIL";
            ws.Cell(rowIdx, 17).Value = row.C3_MinTimeMatch ? "PASS" : "FAIL";
            ws.Cell(rowIdx, 18).Value = row.C4_WILMatch ? "PASS" : "FAIL";
            ws.Cell(rowIdx, 19).Value = row.C5_HeqfMatch ? "PASS" : "FAIL";
            ws.Cell(rowIdx, 20).Value = row.C5_ExpectedHeqf ?? "";
            ws.Cell(rowIdx, 21).Value = row.C6_SubsidyMatch ? "PASS" : "FAIL";
            ws.Cell(rowIdx, 22).Value = string.Join(", ", row.FailedControls);
            ws.Cell(rowIdx, 23).Value = row.ValidationResult;

            var bg = row.ValidationResult == "PASS" ? XLColor.FromHtml("#F3FFF3") : XLColor.FromHtml("#FFF3F3");
            ws.Range(rowIdx, 1, rowIdx, 23).Style.Fill.BackgroundColor = bg;
            ws.Cell(rowIdx, 23).Style.Font.Bold = true;
            ws.Cell(rowIdx, 23).Style.Font.FontColor =
                row.ValidationResult == "PASS" ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");
        }

        public byte[] ExportRule38Csv(Rule38ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Validation_Number,Qual_Code,Qual_Name,Approval,Qual_Type,Qual_Min_Time,Qual_WIL,Qual_HEQF,Qual_Subsidy,PQM_Match,PQM_Type,PQM_Min_Time,PQM_WIL,PQM_Accreditation,PQM_Subsidy,C2_Type,C3_Time,C4_WIL,C5_HEQF,C5_Expected,C6_Subsidy,Failed_Controls,Result");
            var rows = exceptionsOnly
                ? summary.ValidationRows.Where(r => r.ValidationResult == "FAIL")
                : summary.ValidationRows.AsEnumerable();
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(",",
                    row.ValidationNumber,
                    CsvEscape(row.QualCode),
                    CsvEscape(row.QualName),
                    CsvEscape(row.ApprovalStatus),
                    CsvEscape(row.QualType),
                    CsvEscape(row.MinTimeTotal),
                    CsvEscape(row.MinTimeWIL),
                    CsvEscape(row.HeqfIndicator),
                    CsvEscape(row.TotalSubsidy),
                    row.HasPqmMatch ? "YES" : "NO",
                    CsvEscape(row.PqmQualType),
                    CsvEscape(row.PqmMinTimeTotal),
                    CsvEscape(row.PqmWIL),
                    CsvEscape(row.PqmAccreditation),
                    CsvEscape(row.PqmTotalSubsidy),
                    row.C2_TypeMatch ? "PASS" : "FAIL",
                    row.C3_MinTimeMatch ? "PASS" : "FAIL",
                    row.C4_WILMatch ? "PASS" : "FAIL",
                    row.C5_HeqfMatch ? "PASS" : "FAIL",
                    CsvEscape(row.C5_ExpectedHeqf),
                    row.C6_SubsidyMatch ? "PASS" : "FAIL",
                    CsvEscape(string.Join("; ", row.FailedControls)),
                    CsvEscape(row.ValidationResult)));
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule34ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Validation_Number,First_Day,Last_Day,c_Days,c_Days_2,Prepared_Census_Date,Actual_Census_Date,Stored_Census_Date,Day_Status,Validation_Status");

            var rows = exceptionsOnly ? summary.Exceptions : summary.ValidationRows;
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(",",
                    row.ValidationNumber,
                    CsvEscape(row.FirstDayValue),
                    CsvEscape(row.LastDayValue),
                    CsvEscape(row.CurrentDays?.ToString()),
                    CsvEscape(row.CurrentDaysHalf?.ToString()),
                    CsvEscape(row.ComputedCensusDate),
                    CsvEscape(row.ActualCensusDate),
                    CsvEscape(row.CensusDateValue),
                    CsvEscape(row.DayStatus),
                    CsvEscape(row.ValidationStatus)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule35ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule35Headers(summary);
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ValidationRows)
            {
                var values = headers.Select(header => header switch
                {
                    "Validation Number" => row.ValidationNumber.ToString(),
                    "Validation Result" => string.Equals(row.DuplicateStatus, "UNIQUE", StringComparison.OrdinalIgnoreCase) ? "PASS" : "FAIL",
                    "Exception Reason" => string.Equals(row.DuplicateStatus, "UNIQUE", StringComparison.OrdinalIgnoreCase) ? "" : "Duplicate value found",
                    "Duplicate Value" => row.DuplicateValue,
                    "Occurrence Count" => row.OccurrenceCount.ToString(),
                    "Duplicate Status" => row.DuplicateStatus,
                    _ => row.DisplayValues.TryGetValue(header, out var value) ? value : null
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule29ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule29Headers(summary);
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.MatchingRows)
            {
                var values = headers.Select(header =>
                {
                    if (string.Equals(header, "Validation_Number", StringComparison.OrdinalIgnoreCase))
                        return row.ValidationNumber.ToString();
                    if (string.Equals(header, "Filter_Value", StringComparison.OrdinalIgnoreCase))
                        return row.FilterValue;
                    if (string.Equals(header, "Breakdown_Value", StringComparison.OrdinalIgnoreCase))
                        return row.BreakdownValue;

                    return row.DisplayValues.TryGetValue(header, out var value) ? value : null;
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule21ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule21Headers(summary);
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.MatchingRows)
            {
                var values = headers.Select(header =>
                    row.DisplayValues.TryGetValue(header, out var value) ? value : null);

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule17ValidationSummary summary) =>
            ExportCsv(ToRule21Summary(summary));

        public byte[] ExportCsv(Rule14ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule14Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ReviewRows)
            {
                var values = headers.Select(header =>
                    row.DisplayValues.TryGetValue(header, out var value) ? value : null);

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule15ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule15Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ReviewRows)
            {
                var values = headers.Select(header =>
                    row.DisplayValues.TryGetValue(header, out var value) ? value : null);

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule16ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule16Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ReviewRows)
            {
                var values = headers.Select(header =>
                    row.DisplayValues.TryGetValue(header, out var value) ? value : null);

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule18ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule18Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ReviewRows)
            {
                var values = headers.Select(header =>
                    row.DisplayValues.TryGetValue(header, out var value) ? value : null);

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule19ValidationSummary summary) =>
            ExportCsv(ToRule29Summary(summary));

        public byte[] ExportCsv(Rule25ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule25Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.FailRows)
            {
                var values = headers.Select(header => header switch
                {
                    "Validation_Number" => row.ValidationNumber.ToString(),
                    "CRSE_CourseCode" => row.CrseCourseCode,
                    "AUDIT_CourseCode" => row.AuditCourseCode,
                    "H16_CourseCode" => row.H16CourseCode,
                    "Reconciliation_Status" => row.ReconciliationStatus,
                    "Issue_Description" => row.IssueDescription,
                    _ => ""
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule23ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule23Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.FailRows)
            {
                var values = headers.Select(header => header switch
                {
                    "Validation_Number" => row.ValidationNumber.ToString(),
                    "STUD_StudentNum" => row.StudStudentNumber,
                    "STUD_QualCode" => row.StudQualificationCode,
                    "STUD_IDNum" => row.StudIdNumber,
                    "AUDIT_StudentNum" => row.AuditStudentNumber,
                    "AUDIT_QualCode" => row.AuditQualificationCode,
                    "AUDIT_IDNum" => row.AuditIdNumber,
                    "H16_StudentNum" => row.H16StudentNumber,
                    "H16_QualCode" => row.H16QualificationCode,
                    "H16_IDNum" => row.H16IdNumber,
                    "Control_Type" => row.ControlType,
                    "Result_Type" => row.ResultType,
                    "Reconciliation_Status" => row.ReconciliationStatus,
                    "Issue_Description" => row.IssueDescription,
                    _ => ""
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule13ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = new[]
            {
                "Validation_Number",
                "Qualification_Code_001",
                "CESM_Major_Field_006",
                "Qualification_Description_003",
                "Step1_CESM_Check",
                "Linked_STUD_001",
                "Step2_Student_Linkage",
                "Overall_Result",
                "Explanation"
            };
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ReviewRows)
            {
                var values = new[]
                {
                    row.ValidationNumber.ToString(),
                    row.QualificationCode001,
                    row.CourseCode030,
                    row.QualificationDescription003,
                    row.FoundationFlag106,
                    row.FoundationCourse091,
                    row.NotebookStatus,
                    row.ValidationResult,
                    row.ValidationExplanation
                };

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule12ValidationSummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Validation Number,CREG._030,CRSE._030,Validation Result,Validation Explanation");

            foreach (var row in summary.ReviewRows)
            {
                var creg = row.DisplayValues.TryGetValue("CREG__030", out var cregValue) ? cregValue : null;
                var crse = row.DisplayValues.TryGetValue("CRSE__030", out var crseValue) ? crseValue : null;
                sb.AppendLine(string.Join(",", new[]
                {
                    CsvEscape(row.ValidationNumber.ToString()),
                    CsvEscape(creg),
                    CsvEscape(crse),
                    CsvEscape(row.ValidationResult),
                    CsvEscape(row.ValidationExplanation)
                }));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule10ValidationSummary summary)
        {
            var detailKeys = summary.ReviewRows
                .SelectMany(row => row.DisplayValues.Keys)
                .Where(key => !string.Equals(key, "Validation_Result", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "Validation_Explanation", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "RULE_LABEL", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "RESULT_BADGE", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(key, "FINAL_RESULT_MESSAGE", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();
            var headers = new List<string> { "Rule", "Validation Result", "Validation Explanation" };
            headers.AddRange(detailKeys.Select(key => GetIntegrityDetailHeader(summary, key)));
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ReviewRows)
            {
                var values = new List<string>
                {
                    row.RuleId.ToString(CultureInfo.InvariantCulture),
                    row.ValidationResult,
                    row.ValidationExplanation
                };
                values.AddRange(detailKeys.Select(key => row.DisplayValues.TryGetValue(key, out var value) ? value ?? "" : ""));
                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule11ValidationSummary summary)
            => ExportRule11Csv(summary, false);

        public byte[] ExportCsv(Rule20ValidationSummary summary)
        {
            var headers = GetRule20DashboardHeaders();
            var previewRows = BuildRule20DashboardPreviewRows(summary.ReviewRows);
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers));

            foreach (var row in previewRows)
            {
                sb.AppendLine(string.Join(",", headers.Select(header => CsvEscape(header switch
                {
                    "DBO_STUD__007" => row.StudentNumber007,
                    "DBO_STUD__008" => row.StudentColumn008,
                    "DBO_STUD__010" => row.StudentColumn010,
                    "DBO_STUD__012" => row.StudentColumn012,
                    "DBO_STUD__026" => row.StudentColumn026,
                    "DBO_STUD__001" => row.QualificationCode001,
                    "DBO_STUD__106" => row.FoundationFlag106,
                    "DBO_CRED__001" => row.BridgeQualificationCode001,
                    "DBO_CRED__030" => row.CourseCode030,
                    "DBO_CRSE__091" => row.FoundationCourse091,
                    "DBO_CRSE__030" => row.CrseCourseCode030,
                    "RESULTS" => row.ValidationResult,
                    _ => ""
                }))));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule22ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule22Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.ReviewRows)
            {
                var values = headers.Select(header => header switch
                {
                    "Validation_Number" => row.ValidationNumber.ToString(),
                    "Control_Type" => row.ControlType,
                    "Control_Definition" => row.ControlDefinition,
                    "Control_Row_Number" => row.SampleNumber.ToString(),
                    "Staff_Number_037" => row.StaffNumber037,
                    "Year_038" => row.Year038,
                    "Col_039" => row.Col039,
                    "Col_040" => row.Col040,
                    "Col_041" => row.Col041,
                    "Col_042" => row.Col042,
                    "Col_046" => row.Col046,
                    "Col_047" => row.Col047,
                    "Col_048" => row.Col048,
                    "Col_094" => row.Col094,
                    "Validation_Result" => row.ValidationResult,
                    "Exception_Reason" => row.ExceptionReason,
                    _ => ""
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule24ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = GetRule24Headers();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.FailRows)
            {
                var values = headers.Select(header => header switch
                {
                    "Validation_Number" => row.ValidationNumber.ToString(),
                    "QUAL_QualCode" => row.QualQualCode,
                    "QUAL_ApprovalStatus" => row.QualApprovalStatus,
                    "STUD_QualCode" => row.StudQualCode,
                    "AUDIT_QualCode" => row.AuditQualCode,
                    "H16_QualCode" => row.H16QualCode,
                    "Control_Type" => row.ControlType,
                    "Reconciliation_Status" => row.ReconciliationStatus,
                    "Issue_Description" => row.IssueDescription,
                    _ => ""
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule27ValidationSummary summary)
        {
            var sb = new StringBuilder();
            var headers = new[] { "Validation_Number", "Filter_Value" }
                .Concat(summary.MatchingRows
                    .SelectMany(r => r.DisplayValues.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                .ToList();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in summary.MatchingRows)
            {
                var values = headers.Select(header =>
                {
                    if (string.Equals(header, "Validation_Number", StringComparison.OrdinalIgnoreCase))
                        return row.ValidationNumber.ToString();
                    if (string.Equals(header, "Filter_Value", StringComparison.OrdinalIgnoreCase))
                        return row.FilterValue;

                    return row.DisplayValues.TryGetValue(header, out var value) ? value : null;
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule32ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            var headers = GetRule32Headers(summary);
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            var rows = exceptionsOnly ? summary.RemainingRows : summary.ExcludedRows.Concat(summary.RemainingRows).ToList();
            foreach (var row in rows)
            {
                var values = headers.Select(header =>
                {
                    if (string.Equals(header, "Validation_Number", StringComparison.OrdinalIgnoreCase))
                        return row.ValidationNumber.ToString();
                    if (string.Equals(header, "Classification", StringComparison.OrdinalIgnoreCase))
                        return row.Classification;
                    if (string.Equals(header, "Error_Type_Value", StringComparison.OrdinalIgnoreCase))
                        return row.ErrorTypeValue;
                    if (string.Equals(header, "Error_Code", StringComparison.OrdinalIgnoreCase))
                        return row.ErrorCode;
                    if (string.Equals(header, "Normalized_Error_Code", StringComparison.OrdinalIgnoreCase))
                        return row.NormalizedErrorCode;

                    return row.DisplayValues.TryGetValue(header, out var value) ? value : null;
                });

                sb.AppendLine(string.Join(",", values.Select(CsvEscape)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportCsv(Rule31ValidationSummary summary, bool exceptionsOnly = false) =>
            ExportCsv(ToRule32Summary(summary), exceptionsOnly);

        // ─── Rule 39 Export ──────────────────────────────────────────────────────

        public byte[] ExportRule39Excel(Rule39ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var headers = new[]
            {
                "#", "STUD Qual Ref", "STUD _007", "STUD _008", "FTE Value", "STUD _012", "STUD _026",
                "QUAL Code", "QUAL Name", "NAL Qual Name", "NAL Aligned Name", "NAL Category", "HEQSF Ref",
                "SAQA ID", "NQF Level", "Credits", "Outcome", "Result", "Reason"
            };
            const int colCount = 19;

            var wsAll = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsAll, 1, "RULE 39 — FIRST-TIME ENTERING STUDENTS vs NON-ALIGNED QUALIFICATIONS", colCount);
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsAll.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            int rowIdx = 3;
            foreach (var r in summary.FlaggedRows.Concat(summary.ClearSampleRows).OrderBy(r => r.RowNumber))
                WriteRule39ExcelRow(wsAll, rowIdx++, r);
            for (int c = 1; c <= colCount; c++) wsAll.Column(c).AdjustToContents();

            var wsEx = wb.Worksheets.Add("FLAGGED");
            StyleHeaderRow(wsEx, 1, "RULE 39 — FLAGGED (Non-Aligned Qual Found)", colCount);
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsEx.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            int exIdx = 3;
            foreach (var r in summary.FlaggedRows) WriteRule39ExcelRow(wsEx, exIdx++, r);
            for (int c = 1; c <= colCount; c++) wsEx.Column(c).AdjustToContents();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 39: FIRST-TIME ENTERING vs NON-ALIGNED QUALIFICATIONS", 2);
            var summaryData = new List<(string Label, string Value)>
            {
                ("Database",                  summary.Database),
                ("STUD Table",                $"{summary.StudTable} | First-time column: {summary.StudFirstTimeColumn} = '{summary.StudFirstTimeValue}'"),
                ("STUD Qual Ref Column",      summary.StudQualRefColumn),
                ("QUAL Table",                summary.QualTable),
                ("QUAL Code Column",          summary.QualCodeColumn),
                ("QUAL Name Column",          summary.QualNameColumn),
                ("NAL Table",                 $"{summary.NalTable} | Category column: {summary.NalCategoryColumn} = '{summary.NalCategoryValue}'"),
                ("Join Key",                  $"STUD.{summary.StudQualRefColumn} = QUAL.{summary.QualCodeColumn} -> NAL.{summary.NalRefColumn}"),
                ("Validation Date",           summary.Timestamp),
                ("",                          ""),
                ("VALIDATION RESULTS",        ""),
                ("STUD Total Records",        summary.StudTotalCount.ToString("N0")),
                ("First-Time Entering (FTE)", summary.TotalValidated.ToString("N0")),
                ("QUAL Total Records",        summary.QualTotalCount.ToString("N0")),
                ("NAL Category Count",        summary.NalCategoryCount.ToString("N0")),
                ("FLAGGED (Non-Aligned)",     summary.FlaggedCount.ToString("N0")),
                ("CLEAR (Not Found)",         summary.ClearCount.ToString("N0")),
                ("Exception Rate",            $"{summary.ExceptionRate:F2}%"),
                ("Status",                    summary.Status)
            };
            if (!string.IsNullOrWhiteSpace(summary.Stud007Column)) summaryData.Insert(3, ("STUD _007 Column", summary.Stud007Column));
            if (!string.IsNullOrWhiteSpace(summary.Stud008Column)) summaryData.Insert(4, ("STUD _008 Column", summary.Stud008Column));
            if (!string.IsNullOrWhiteSpace(summary.Stud012Column)) summaryData.Insert(5, ("STUD _012 Column", summary.Stud012Column));
            if (!string.IsNullOrWhiteSpace(summary.Stud026Column)) summaryData.Insert(6, ("STUD _026 Column", summary.Stud026Column));
            int sumRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                {
                    var hdrCell = wsSummary.Cell(sumRow, 1);
                    hdrCell.Value = label;
                    hdrCell.Style.Font.Bold = true;
                    hdrCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                    hdrCell.Style.Font.FontColor = XLColor.White;
                    wsSummary.Range(sumRow, 1, sumRow, 2).Merge();
                }
                else if (label != "")
                {
                    wsSummary.Cell(sumRow, 1).Value = label;
                    wsSummary.Cell(sumRow, 1).Style.Font.Bold = true;
                    wsSummary.Cell(sumRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sumRow, 2).Value = value;
                    if (label == "Status")
                    {
                        wsSummary.Cell(sumRow, 2).Style.Fill.BackgroundColor =
                            value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(sumRow, 2).Style.Font.Bold = true;
                    }
                }
                sumRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 70;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void WriteRule39ExcelRow(IXLWorksheet ws, int rowIdx, Rule39ValidationRowViewModel row)
        {
            ws.Cell(rowIdx, 1).Value  = row.RowNumber;
            ws.Cell(rowIdx, 2).Value  = row.StudQualRef;
            ws.Cell(rowIdx, 3).Value  = row.Stud007Value ?? "";
            ws.Cell(rowIdx, 4).Value  = row.Stud008Value ?? "";
            ws.Cell(rowIdx, 5).Value  = row.Stud010Value;
            ws.Cell(rowIdx, 6).Value  = row.Stud012Value ?? "";
            ws.Cell(rowIdx, 7).Value  = row.Stud026Value ?? "";
            ws.Cell(rowIdx, 8).Value  = row.QualCodeValue ?? "";
            ws.Cell(rowIdx, 9).Value  = row.QualNameValue ?? "";
            ws.Cell(rowIdx, 10).Value = row.NalQualName ?? "";
            ws.Cell(rowIdx, 11).Value = row.NalAlignedName ?? "";
            ws.Cell(rowIdx, 12).Value = row.NalCategory ?? "";
            ws.Cell(rowIdx, 13).Value = row.NalHeqsfRef ?? "";
            ws.Cell(rowIdx, 14).Value = row.NalSaqaId ?? "";
            ws.Cell(rowIdx, 15).Value = row.NalNqf ?? "";
            ws.Cell(rowIdx, 16).Value = row.NalCredits ?? "";
            ws.Cell(rowIdx, 17).Value = row.NalOutcome ?? "";
            ws.Cell(rowIdx, 18).Value = row.Result;
            ws.Cell(rowIdx, 19).Value = row.ExceptionReason ?? "";

            var isFlagged = string.Equals(row.Result, "FLAGGED", StringComparison.OrdinalIgnoreCase);
            var bg = isFlagged ? XLColor.FromHtml("#FFF3F3") : XLColor.FromHtml("#F3FFF3");
            ws.Range(rowIdx, 1, rowIdx, 19).Style.Fill.BackgroundColor = bg;
            ws.Cell(rowIdx, 18).Style.Font.Bold = true;
            ws.Cell(rowIdx, 18).Style.Font.FontColor = isFlagged ? XLColor.FromHtml("#B71C1C") : XLColor.FromHtml("#1B5E20");
        }

        public byte[] ExportRule39Csv(Rule39ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Row_No,STUD_Qual_Ref,STUD_007,STUD_008,First_Time_Value,STUD_012,STUD_026,QUAL_Code,QUAL_Name,NAL_Qual_Name,NAL_Aligned_Name,NAL_Category,HEQSF_Ref,SAQA_ID,NQF_Level,Credits,Outcome,Result,Exception_Reason");
            var rows = exceptionsOnly
                ? summary.FlaggedRows.AsEnumerable()
                : summary.FlaggedRows.Concat(summary.ClearSampleRows).OrderBy(row => row.RowNumber);
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(",",
                    row.RowNumber,
                    CsvEscape(row.StudQualRef),
                    CsvEscape(row.Stud007Value),
                    CsvEscape(row.Stud008Value),
                    CsvEscape(row.Stud010Value),
                    CsvEscape(row.Stud012Value),
                    CsvEscape(row.Stud026Value),
                    CsvEscape(row.QualCodeValue),
                    CsvEscape(row.QualNameValue),
                    CsvEscape(row.NalQualName),
                    CsvEscape(row.NalAlignedName),
                    CsvEscape(row.NalCategory),
                    CsvEscape(row.NalHeqsfRef),
                    CsvEscape(row.NalSaqaId),
                    CsvEscape(row.NalNqf),
                    CsvEscape(row.NalCredits),
                    CsvEscape(row.NalOutcome),
                    CsvEscape(row.Result),
                    CsvEscape(row.ExceptionReason)));
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── SQL Export ───────────────────────────────────────────────────────
        public byte[] ExportSql(string sql) => Encoding.UTF8.GetBytes(sql);

        private static Rule29ValidationSummary ToRule29Summary(Rule21ValidationSummary summary) =>
            new()
            {
                Success = summary.Success,
                TotalValidated = summary.TotalValidated,
                MatchingCount = summary.MatchingCount,
                DisplayedCount = summary.DisplayedCount,
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
                Warning = summary.Warning,
                Error = summary.Error,
                Breakdown = summary.Breakdown
                    .Select(item => new Rule29BreakdownItemViewModel
                    {
                        Value = item.Value,
                        Count = item.Count
                    })
                    .ToList(),
                MatchingRows = summary.MatchingRows
                    .Select(row => new Rule29ValidationRowRecord
                    {
                        ValidationNumber = row.ValidationNumber,
                        FilterValue = row.FilterValue,
                        BreakdownValue = row.BreakdownValue,
                        DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
                    })
                    .ToList()
            };

        private static Rule21ValidationSummary ToRule21Summary(Rule17ValidationSummary summary) =>
            new()
            {
                Success = summary.Success,
                TotalValidated = summary.TotalValidated,
                MatchingCount = summary.MatchingCount,
                DisplayedCount = summary.DisplayedCount,
                IsPreviewOnly = summary.IsPreviewOnly,
                PreviewLimit = summary.PreviewLimit,
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
                Warning = summary.Warning,
                Error = summary.Error,
                Breakdown = summary.Breakdown
                    .Select(item => new Rule21BreakdownItemViewModel
                    {
                        Value = item.Value,
                        Count = item.Count
                    })
                    .ToList(),
                MatchingRows = summary.MatchingRows
                    .Select(row => new Rule21ValidationRowRecord
                    {
                        ValidationNumber = row.ValidationNumber,
                        FilterValue = row.FilterValue,
                        BreakdownValue = row.BreakdownValue,
                        DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
                    })
                    .ToList()
            };

        private static Rule29ValidationSummary ToRule29Summary(Rule19ValidationSummary summary) =>
            new()
            {
                Success = summary.Success,
                TotalValidated = summary.TotalValidated,
                MatchingCount = summary.MatchingCount,
                DisplayedCount = summary.DisplayedCount,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                TableName = $"{summary.StudTable} -> {summary.QualTable}",
                FilterColumn = summary.FulfilledColumn,
                FilterValue = summary.FulfilledValue,
                BreakdownColumn = summary.QualTypeColumn,
                SampleSize = summary.TotalValidated,
                ShowAllRecords = summary.ShowAllRecords,
                Sampled = false,
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                Warning = summary.Warning,
                Error = summary.Error,
                Breakdown = summary.Breakdown
                    .Select(item => new Rule29BreakdownItemViewModel
                    {
                        Value = item.Value,
                        Count = item.Count
                    })
                    .ToList(),
                MatchingRows = summary.MatchingRows
                    .Select(row => new Rule29ValidationRowRecord
                    {
                        ValidationNumber = row.ValidationNumber,
                        FilterValue = row.FulfilledValue,
                        BreakdownValue = row.QualTypeValue,
                        DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
                    })
                    .ToList()
            };

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

        private static List<string> GetRule32Headers(Rule32ValidationSummary summary) =>
            new[] { "Validation_Number", "Classification", "Error_Type_Value", "Error_Code", "Normalized_Error_Code" }
                .Concat(summary.ExcludedRows
                    .Concat(summary.RemainingRows)
                    .SelectMany(r => r.DisplayValues.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                .ToList();

        private static List<string> GetRule35Headers(Rule35ValidationSummary summary) =>
            new[] { "Validation Number", "Validation Result", "Exception Reason", "Duplicate Value", "Occurrence Count", "Duplicate Status" }
                .Concat(summary.ValidationRows
                    .SelectMany(r => r.DisplayValues.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                .ToList();

        private static void WriteRule35HeaderRow(IXLWorksheet ws, int row, IReadOnlyList<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule35Rows(IXLWorksheet ws, int startRow, IReadOnlyCollection<Rule35ValidationRowRecord> rows, IReadOnlyList<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation Number" => row.ValidationNumber.ToString(),
                        "Validation Result" => string.Equals(row.DuplicateStatus, "UNIQUE", StringComparison.OrdinalIgnoreCase) ? "PASS" : "FAIL",
                        "Exception Reason" => string.Equals(row.DuplicateStatus, "UNIQUE", StringComparison.OrdinalIgnoreCase) ? "" : "Duplicate value found",
                        "Duplicate Value" => row.DuplicateValue,
                        "Occurrence Count" => row.OccurrenceCount.ToString(),
                        "Duplicate Status" => row.DuplicateStatus,
                        _ => row.DisplayValues.TryGetValue(header, out var displayValue) ? displayValue : null
                    };

                    ws.Cell(rowIndex, i + 1).Value = value ?? "";
                }

                var fillColor = string.Equals(row.DuplicateStatus, "DUPLICATE", StringComparison.OrdinalIgnoreCase)
                    ? XLColor.FromHtml("#FFF3F3")
                    : XLColor.FromHtml("#F3FFF3");
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = fillColor;
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static List<string> GetRule29Headers(Rule29ValidationSummary summary) =>
            new[] { "Validation_Number", "Filter_Value", "Breakdown_Value" }
                .Concat(summary.MatchingRows
                    .SelectMany(r => r.DisplayValues.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                .ToList();

        private static List<string> GetRule21Headers(Rule21ValidationSummary summary)
        {
            var preferred = new[] { "Validation_Result" };
            return preferred
                .Concat(summary.MatchingRows
                    .SelectMany(r => r.DisplayValues.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(header => Array.IndexOf(preferred, header) >= 0 ? Array.IndexOf(preferred, header) : (header.StartsWith("_", StringComparison.OrdinalIgnoreCase) ? 100 : 200))
                .ThenBy(header => header, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> GetRule14Headers() =>
            new()
            {
                "Control_Type",
                "Control_Label",
                "Validation_Result",
                "Validation_Explanation",
                "CRSE__030",
                "CRSE__031",
                "CREG__030"
            };

        private static List<string> GetRule15Headers() =>
            new()
            {
                "Control_Type",
                "Control_Label",
                "Validation_Result",
                "Validation_Explanation",
                "QUAL__001",
                "QUAL__004",
                "CRED__001",
                "CRED__030",
                "CREG__001",
                "CREG__030"
            };

        private static List<string> GetRule16Headers() =>
            new()
            {
                "Control_Type",
                "Control_Label",
                "Validation_Result",
                "Validation_Explanation",
                "STUD__001",
                "STUD__007",
                "STUD__025",
                "STUD__024",
                "BRIDGE__001",
                "BRIDGE__030",
                "CRSE__030",
                "CRSE__091"
            };

        private static List<string> GetRule18Headers() =>
            new()
            {
                "Control_Type",
                "Control_Label",
                "Validation_Result",
                "Validation_Explanation",
                "STUD__001",
                "STUD__007",
                "STUD__019",
                "STUD__024",
                "BRIDGE__001",
                "BRIDGE__030",
                "CRSE__030",
                "CRSE__091"
            };

        private static List<string> GetRule24Headers() =>
            new()
            {
                "Validation_Number",
                "QUAL_QualCode",
                "QUAL_ApprovalStatus",
                "STUD_QualCode",
                "AUDIT_QualCode",
                "H16_QualCode",
                "Control_Type",
                "Reconciliation_Status",
                "Issue_Description"
            };

        private static List<string> GetRule23Headers() =>
            new()
            {
                "Validation_Number",
                "STUD_StudentNum",
                "STUD_QualCode",
                "STUD_IDNum",
                "AUDIT_StudentNum",
                "AUDIT_QualCode",
                "AUDIT_IDNum",
                "H16_StudentNum",
                "H16_QualCode",
                "H16_IDNum",
                "Control_Type",
                "Result_Type",
                "Reconciliation_Status",
                "Issue_Description"
            };

        private static List<string> GetRule20Headers() =>
            new()
            {
                "Validation_Number",
                "Part_Code",
                "Part_Title",
                "Part_Description",
                "Student_Number_007",
                "Student_Column_008",
                "Student_Column_010",
                "Student_Column_012",
                "Student_Column_026",
                "Qualification_Code_001",
                "Foundation_Flag_106",
                "Bridge_Qualification_Code_001",
                "Course_Code_030",
                "Foundation_Course_091",
                "Crse_Course_Code_030",
                "Name_019",
                "ID_Number_024",
                "Qualification_Description_003",
                "Qualification_Type_005",
                "Student_Type",
                "Notebook_Status",
                "Validation_Result",
                "Validation_Explanation"
            };

        private static List<string> GetRule20DashboardHeaders() =>
            new()
            {
                "DBO_STUD__007",
                "DBO_STUD__008",
                "DBO_STUD__010",
                "DBO_STUD__012",
                "DBO_STUD__026",
                "DBO_STUD__001",
                "DBO_STUD__106",
                "DBO_CRED__001",
                "DBO_CRED__030",
                "DBO_CRSE__091",
                "DBO_CRSE__030",
                "RESULTS"
            };

        private static List<string> GetRule22Headers() =>
            new()
            {
                "Validation_Number",
                "Control_Type",
                "Control_Definition",
                "Control_Row_Number",
                "Staff_Number_037",
                "Year_038",
                "Col_039",
                "Col_040",
                "Col_041",
                "Col_042",
                "Col_046",
                "Col_047",
                "Col_048",
                "Col_094",
                "Validation_Result",
                "Exception_Reason"
            };

        private static List<string> GetRule25Headers() =>
            new()
            {
                "Validation_Number",
                "CRSE_CourseCode",
                "AUDIT_CourseCode",
                "H16_CourseCode",
                "Reconciliation_Status",
                "Issue_Description"
            };

        private static void WriteRule32HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule32Rows(IXLWorksheet ws, int startRow, List<Rule32ValidationRowRecord> rows, List<string> headers, string fillColor)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "Classification" => row.Classification,
                        "Error_Type_Value" => row.ErrorTypeValue,
                        "Error_Code" => row.ErrorCode,
                        "Normalized_Error_Code" => row.NormalizedErrorCode,
                        _ => row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fillColor);
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule29HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule21HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule16HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule14HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule15HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule18HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule29Rows(IXLWorksheet ws, int startRow, List<Rule29ValidationRowRecord> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "Filter_Value" => row.FilterValue,
                        "Breakdown_Value" => row.BreakdownValue,
                        _ => row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule21Rows(IXLWorksheet ws, int startRow, List<Rule21ValidationRowRecord> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : "";
                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                if (row.DisplayValues.TryGetValue("Validation_Result", out var validationResult))
                {
                    var fill = string.Equals(validationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                        ? "#FFF3F3"
                        : "#F3FFF3";
                    ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                }

                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule16Rows(IXLWorksheet ws, int startRow, List<Rule16ValidationRowRecord> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : "";
                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "#FFF3F3"
                    : "#F3FFF3";
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }

            SetRule16ColumnWidths(ws);
        }

        private static void WriteRule14Rows(IXLWorksheet ws, int startRow, List<Rule14ValidationRowRecord> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : "";
                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "#FFF3F3"
                    : "#F3FFF3";
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }

            SetRule14ColumnWidths(ws);
        }

        private static void WriteRule15Rows(IXLWorksheet ws, int startRow, List<Rule15ValidationRowRecord> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : "";
                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "#FFF3F3"
                    : "#F3FFF3";
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }

            SetRule15ColumnWidths(ws);
        }

        private static void WriteRule18Rows(IXLWorksheet ws, int startRow, List<Rule18ValidationRowRecord> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : "";
                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "#FFF3F3"
                    : "#F3FFF3";
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }

            SetRule18ColumnWidths(ws);
        }

        private static void WriteRule16ControlSheet(XLWorkbook workbook, string sheetName, string title, List<Rule16ValidationRowRecord> rows, List<string> headers)
        {
            var ws = workbook.Worksheets.Add(sheetName);
            StyleHeaderRow(ws, 1, title, headers.Count);
            WriteRule16HeaderRow(ws, 2, headers);
            WriteRule16Rows(ws, 3, rows, headers);
        }

        private static void WriteRule14ControlSheet(XLWorkbook workbook, string sheetName, string title, List<Rule14ValidationRowRecord> rows, List<string> headers)
        {
            var ws = workbook.Worksheets.Add(sheetName);
            StyleHeaderRow(ws, 1, title, headers.Count);
            WriteRule14HeaderRow(ws, 2, headers);
            WriteRule14Rows(ws, 3, rows, headers);
        }

        private static void WriteRule15ControlSheet(XLWorkbook workbook, string sheetName, string title, List<Rule15ValidationRowRecord> rows, List<string> headers)
        {
            var ws = workbook.Worksheets.Add(sheetName);
            StyleHeaderRow(ws, 1, title, headers.Count);
            WriteRule15HeaderRow(ws, 2, headers);
            WriteRule15Rows(ws, 3, rows, headers);
        }

        private static void WriteRule18ControlSheet(XLWorkbook workbook, string sheetName, string title, List<Rule18ValidationRowRecord> rows, List<string> headers)
        {
            var ws = workbook.Worksheets.Add(sheetName);
            StyleHeaderRow(ws, 1, title, headers.Count);
            WriteRule18HeaderRow(ws, 2, headers);
            WriteRule18Rows(ws, 3, rows, headers);
        }

        private static void SetRule14ColumnWidths(IXLWorksheet ws)
        {
            ws.Column(1).Width = 14;
            ws.Column(2).Width = 40;
            ws.Column(3).Width = 16;
            ws.Column(4).Width = 52;
            ws.Column(5).Width = 16;
            ws.Column(6).Width = 14;
            ws.Column(7).Width = 16;
        }

        private static void SetRule15ColumnWidths(IXLWorksheet ws)
        {
            ws.Column(1).Width = 14;
            ws.Column(2).Width = 40;
            ws.Column(3).Width = 16;
            ws.Column(4).Width = 52;
            ws.Column(5).Width = 16;
            ws.Column(6).Width = 14;
            ws.Column(7).Width = 16;
            ws.Column(8).Width = 16;
            ws.Column(9).Width = 16;
            ws.Column(10).Width = 16;
        }

        private static void SetRule16ColumnWidths(IXLWorksheet ws)
        {
            ws.Column(1).Width = 14;
            ws.Column(2).Width = 28;
            ws.Column(3).Width = 16;
            ws.Column(4).Width = 28;
            ws.Column(5).Width = 16;
            ws.Column(6).Width = 16;
            ws.Column(7).Width = 14;
            ws.Column(8).Width = 14;
            ws.Column(9).Width = 16;
            ws.Column(10).Width = 16;
            ws.Column(11).Width = 16;
            ws.Column(12).Width = 14;
        }

        private static void SetRule18ColumnWidths(IXLWorksheet ws)
        {
            ws.Column(1).Width = 14;
            ws.Column(2).Width = 28;
            ws.Column(3).Width = 16;
            ws.Column(4).Width = 28;
            ws.Column(5).Width = 16;
            ws.Column(6).Width = 16;
            ws.Column(7).Width = 14;
            ws.Column(8).Width = 14;
            ws.Column(9).Width = 16;
            ws.Column(10).Width = 16;
            ws.Column(11).Width = 16;
            ws.Column(12).Width = 14;
        }

        private static void WriteRule24HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule23HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule20HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule22HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule25HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule24Rows(IXLWorksheet ws, int startRow, List<Rule24ReconciliationRowViewModel> rows, List<string> headers, string fillColor)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "QUAL_QualCode" => row.QualQualCode,
                        "QUAL_ApprovalStatus" => row.QualApprovalStatus,
                        "STUD_QualCode" => row.StudQualCode,
                        "AUDIT_QualCode" => row.AuditQualCode,
                        "H16_QualCode" => row.H16QualCode,
                        "Control_Type" => row.ControlType,
                        "Reconciliation_Status" => row.ReconciliationStatus,
                        "Issue_Description" => row.IssueDescription,
                        _ => ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fillColor);
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule23Rows(IXLWorksheet ws, int startRow, List<Rule23ReconciliationRowViewModel> rows, List<string> headers, string fillColor)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "STUD_StudentNum" => row.StudStudentNumber,
                        "STUD_QualCode" => row.StudQualificationCode,
                        "STUD_IDNum" => row.StudIdNumber,
                        "AUDIT_StudentNum" => row.AuditStudentNumber,
                        "AUDIT_QualCode" => row.AuditQualificationCode,
                        "AUDIT_IDNum" => row.AuditIdNumber,
                        "H16_StudentNum" => row.H16StudentNumber,
                        "H16_QualCode" => row.H16QualificationCode,
                        "H16_IDNum" => row.H16IdNumber,
                        "Control_Type" => row.ControlType,
                        "Result_Type" => row.ResultType,
                        "Reconciliation_Status" => row.ReconciliationStatus,
                        "Issue_Description" => row.IssueDescription,
                        _ => ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fillColor);
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule20Rows(IXLWorksheet ws, int startRow, List<Rule20ReviewRowViewModel> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "Part_Code" => row.PartCode,
                        "Part_Title" => row.PartTitle,
                        "Part_Description" => row.PartDescription,
                        "Student_Number_007" => row.StudentNumber007,
                        "Student_Column_008" => row.StudentColumn008,
                        "Student_Column_010" => row.StudentColumn010,
                        "Student_Column_012" => row.StudentColumn012,
                        "Student_Column_026" => row.StudentColumn026,
                        "Qualification_Code_001" => row.QualificationCode001,
                        "Foundation_Flag_106" => row.FoundationFlag106,
                        "Bridge_Qualification_Code_001" => row.BridgeQualificationCode001,
                        "Course_Code_030" => row.CourseCode030,
                        "Foundation_Course_091" => row.FoundationCourse091,
                        "Crse_Course_Code_030" => row.CrseCourseCode030,
                        "Name_019" => row.Name019,
                        "ID_Number_024" => row.IdNumber024,
                        "Qualification_Description_003" => row.QualificationDescription003,
                        "Qualification_Type_005" => row.QualificationType005,
                        "Student_Type" => row.StudentType,
                        "Notebook_Status" => row.NotebookStatus,
                        "Validation_Result" => row.ValidationResult,
                        "Validation_Explanation" => row.ValidationExplanation,
                        _ => ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "#FFF3F3"
                    : "#F3FFF3";
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule20DashboardHeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule20DashboardRows(IXLWorksheet ws, int startRow, List<Rule20ReviewRowViewModel> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "DBO_STUD__007" => row.StudentNumber007,
                        "DBO_STUD__008" => row.StudentColumn008,
                        "DBO_STUD__010" => row.StudentColumn010,
                        "DBO_STUD__012" => row.StudentColumn012,
                        "DBO_STUD__026" => row.StudentColumn026,
                        "DBO_STUD__001" => row.QualificationCode001,
                        "DBO_STUD__106" => row.FoundationFlag106,
                        "DBO_CRED__001" => row.BridgeQualificationCode001,
                        "DBO_CRED__030" => row.CourseCode030,
                        "DBO_CRSE__091" => row.FoundationCourse091,
                        "DBO_CRSE__030" => row.CrseCourseCode030,
                        "RESULTS" => row.ValidationResult,
                        _ => ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "#FFF3F3"
                    : "#F3FFF3";
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static List<Rule20ReviewRowViewModel> BuildRule20DashboardPreviewRows(List<Rule20ReviewRowViewModel> rows)
        {
            return (rows ?? new List<Rule20ReviewRowViewModel>()).ToList();
        }

        private static void WriteRule22Rows(IXLWorksheet ws, int startRow, List<Rule22ReviewRowViewModel> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "Control_Type" => row.ControlType,
                        "Control_Definition" => row.ControlDefinition,
                        "Control_Row_Number" => row.SampleNumber.ToString(),
                        "Staff_Number_037" => row.StaffNumber037,
                        "Year_038" => row.Year038,
                        "Col_039" => row.Col039,
                        "Col_040" => row.Col040,
                        "Col_041" => row.Col041,
                        "Col_042" => row.Col042,
                        "Col_046" => row.Col046,
                        "Col_047" => row.Col047,
                        "Col_048" => row.Col048,
                        "Col_094" => row.Col094,
                        "Validation_Result" => row.ValidationResult,
                        "Exception_Reason" => row.ExceptionReason,
                        _ => ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "#FFF3F3"
                    : "#F3FFF3";
                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule25Rows(IXLWorksheet ws, int startRow, List<Rule25ReconciliationRowViewModel> rows, List<string> headers, string fillColor)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "CRSE_CourseCode" => row.CrseCourseCode,
                        "AUDIT_CourseCode" => row.AuditCourseCode,
                        "H16_CourseCode" => row.H16CourseCode,
                        "Reconciliation_Status" => row.ReconciliationStatus,
                        "Issue_Description" => row.IssueDescription,
                        _ => ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                ws.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml(fillColor);
                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static void WriteRule27HeaderRow(IXLWorksheet ws, int row, List<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteRule27Rows(IXLWorksheet ws, int startRow, List<Rule27ValidationRowRecord> rows, List<string> headers)
        {
            var rowIndex = startRow;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var value = header switch
                    {
                        "Validation_Number" => row.ValidationNumber.ToString(),
                        "Filter_Value" => row.FilterValue,
                        _ => row.DisplayValues.TryGetValue(header, out var currentValue) ? currentValue ?? "" : ""
                    };

                    ws.Cell(rowIndex, i + 1).Value = value;
                }

                rowIndex++;
            }

            for (var c = 1; c <= headers.Count; c++)
                ws.Column(c).AdjustToContents();
        }

        private static Rule32ValidationSummary ToRule32Summary(Rule31ValidationSummary summary) =>
            new()
            {
                Success = summary.Success,
                TotalValidated = summary.TotalValidated,
                TotalFatal = summary.TotalFatal,
                ExcludedCount = summary.ExcludedCount,
                RemainingCount = summary.RemainingCount,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                TableName = summary.TableName,
                ErrorTypeColumn = summary.ErrorTypeColumn,
                ErrorColumn = summary.ErrorColumn,
                ErrorTypeValue = summary.ErrorTypeValue,
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                Exclusions = summary.Exclusions.ToList(),
                NormalizedExclusions = summary.NormalizedExclusions.ToList(),
                ExcludedBreakdown = summary.ExcludedBreakdown
                    .Select(item => new Rule32BreakdownItemViewModel { ErrorCode = item.ErrorCode, Count = item.Count })
                    .ToList(),
                RemainingBreakdown = summary.RemainingBreakdown
                    .Select(item => new Rule32BreakdownItemViewModel { ErrorCode = item.ErrorCode, Count = item.Count })
                    .ToList(),
                ExcludedRows = summary.ExcludedRows.Select(ToRule32Row).ToList(),
                RemainingRows = summary.RemainingRows.Select(ToRule32Row).ToList(),
                Warning = summary.Warning,
                Error = summary.Error
            };

        private static Rule16ValidationSummary ToRule16Summary(Rule15ValidationSummary summary) =>
            new()
            {
                Success = summary.Success,
                StudRecordCount = summary.StudRecordCount,
                BridgeRecordCount = summary.BridgeRecordCount,
                CrseRecordCount = summary.CrseRecordCount,
                UnfulfilledPopulationCount = summary.UnfulfilledPopulationCount,
                TotalRequested = summary.TotalRequested,
                TotalValidated = summary.TotalValidated,
                DisplayedCount = summary.DisplayedCount,
                IsPreviewOnly = summary.IsPreviewOnly,
                PreviewLimit = summary.PreviewLimit,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                StudTable = summary.StudTable,
                BridgeTable = summary.BridgeTable,
                CrseTable = summary.CrseTable,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText = summary.RuleModeText,
                ProcedureSteps = summary.ProcedureSteps?.ToList() ?? new List<string>(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = (summary.ControlSummaries ?? new List<Rule15ControlSummaryItemViewModel>())
                    .Select(item => new Rule16ControlSummaryItemViewModel
                    {
                        ControlType = item.ControlType,
                        ControlLabel = item.ControlLabel,
                        CriteriaText = item.CriteriaText,
                        RequestedCount = item.RequestedCount,
                        AvailableCount = item.AvailableCount,
                        AchievedCount = item.AchievedCount,
                        TotalCount = item.TotalCount,
                        PassCount = item.PassCount,
                        FailCount = item.FailCount,
                        Status = item.Status
                    })
                    .ToList(),
                ReviewRows = (summary.ReviewRows ?? new List<Rule15ValidationRowRecord>())
                    .Select(row => new Rule16ValidationRowRecord
                    {
                        ValidationNumber = row.ValidationNumber,
                        ControlType = row.ControlType,
                        ControlLabel = row.ControlLabel,
                        ValidationResult = row.ValidationResult,
                        ValidationExplanation = row.ValidationExplanation,
                        DisplayValues = new Dictionary<string, string?>(row.DisplayValues ?? new Dictionary<string, string?>(), StringComparer.OrdinalIgnoreCase)
                    })
                    .ToList(),
                Warning = summary.Warning,
                Error = summary.Error
            };

        private static Rule32ValidationRowRecord ToRule32Row(Rule31ValidationRowRecord row) =>
            new()
            {
                ValidationNumber = row.ValidationNumber,
                ErrorTypeValue = row.ErrorTypeValue,
                ErrorCode = row.ErrorCode,
                NormalizedErrorCode = row.NormalizedErrorCode,
                Classification = row.Classification,
                ErrorMessage = row.ErrorMessage,
                Description = row.Description,
                ElementInformation = row.ElementInformation,
                DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
            };

        private static string CsvEscape(string? val)
        {
            if (string.IsNullOrEmpty(val))
                return "";
            if (val.Contains(',') || val.Contains('"') || val.Contains('\n'))
                return $"\"{val.Replace("\"", "\"\"")}\"";
            return val;
        }

        // ─── Rule 51: VALPAC Data in Production ────────────────────────────────

        private static string ResolveRule11PopulationType(
            Rule11ValidationSummary summary,
            string? populationType,
            string? qualHeqfType)
        {
            if (!string.IsNullOrWhiteSpace(populationType))
                return populationType;

            var postgraduateTypeCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var code in (summary.QualTypeCodesText ?? "").Split(new[] { ',', '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = code.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    postgraduateTypeCodes.Add(trimmed.ToUpperInvariant());
            }

            var heqfType = (qualHeqfType ?? "").Trim().ToUpperInvariant();
            return postgraduateTypeCodes.Contains(heqfType) ? "Postgraduate" : "Undergraduate";
        }

        public byte[] ExportRule51Excel(Rule51ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var wsResults = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsResults, 1, "RULE 51 VALIDATION RESULTS — VALPAC IN PRODUCTION", 9);
            var headers = new[] { "Validation #", "STUD VALPAC _007", "STUD VALPAC _008", "STUD VALPAC _001", "STUD VALPAC ColYear", "PROD IAGSTNO", "PROD IADIDNO", "PROD IAGQUAL", "PROD IAGCYR", "Result" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsResults.Cell(2, i + 1);
                cell.Value = headers[i]; cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            int rowIndex = 3;
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                wsResults.Cell(rowIndex, 1).Value = row.ValidationNumber;
                wsResults.Cell(rowIndex, 2).Value = v.TryGetValue("VALPAC__007", out var v7) ? v7 ?? "" : "";
                wsResults.Cell(rowIndex, 3).Value = v.TryGetValue("VALPAC__008", out var v8) ? v8 ?? "" : "";
                wsResults.Cell(rowIndex, 4).Value = v.TryGetValue("VALPAC__001", out var v1) ? v1 ?? "" : "";
                wsResults.Cell(rowIndex, 5).Value = v.TryGetValue("VALPAC_COLYEAR", out var vy) ? vy ?? "" : "";
                wsResults.Cell(rowIndex, 6).Value = v.TryGetValue("PROD_IAGSTNO", out var ps) ? ps ?? "" : "";
                wsResults.Cell(rowIndex, 7).Value = v.TryGetValue("PROD_IADIDNO", out var pi) ? pi ?? "" : "";
                wsResults.Cell(rowIndex, 8).Value = v.TryGetValue("PROD_IAGQUAL", out var pq) ? pq ?? "" : "";
                wsResults.Cell(rowIndex, 9).Value = v.TryGetValue("PROD_IAGCYR", out var pc) ? pc ?? "" : "";
                wsResults.Cell(rowIndex, 10).Value = row.ValidationResult;
                var fill = string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase) ? XLColor.FromHtml("#FFF3F3") : XLColor.FromHtml("#F3FFF3");
                wsResults.Range(rowIndex, 1, rowIndex, 10).Style.Fill.BackgroundColor = fill;
                rowIndex++;
            }
            for (int c = 1; c <= 10; c++) wsResults.Column(c).AdjustToContents();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 51: VALPAC DATA IN PRODUCTION", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database), ("VALPAC Table", summary.ValpacTable), ("PRODUCTION Table", summary.ProdTable),
                ("Column Mapping", summary.TableLinkageText), ("Validation Date", summary.Timestamp), ("", ""),
                ("VALIDATION RESULTS", ""), ("Total Tested", summary.TotalValidated.ToString("N0")),
                ("PASS (found in Production)", summary.PassCount.ToString("N0")), ("FAIL (not in Production)", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"), ("Status", summary.Status)
            };
            int sRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                { var hc = wsSummary.Cell(sRow, 1); hc.Value = label; hc.Style.Font.Bold = true; hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000"); hc.Style.Font.FontColor = XLColor.White; wsSummary.Range(sRow, 1, sRow, 2).Merge(); }
                else if (label != "")
                {
                    wsSummary.Cell(sRow, 1).Value = label; wsSummary.Cell(sRow, 1).Style.Font.Bold = true; wsSummary.Cell(sRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sRow, 2).Value = value;
                    if (label == "Status") { wsSummary.Cell(sRow, 2).Style.Fill.BackgroundColor = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2"); wsSummary.Cell(sRow, 2).Style.Font.Bold = true; }
                }
                sRow++;
            }
            wsSummary.Column(1).Width = 32; wsSummary.Column(2).Width = 70;

            var failRows = summary.ReviewRows.Where(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).ToList();
            if (failRows.Any())
            {
                var wsEx = wb.Worksheets.Add("Exceptions");
                StyleHeaderRow(wsEx, 1, "RULE 51 EXCEPTIONS — VALPAC NOT IN PRODUCTION", 10);
                for (int i = 0; i < headers.Length; i++)
                { var c = wsEx.Cell(2, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true; c.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000"); c.Style.Font.FontColor = XLColor.White; }
                int exRow = 3;
                foreach (var row in failRows)
                {
                    var v = row.DisplayValues;
                    wsEx.Cell(exRow, 1).Value = row.ValidationNumber;
                    wsEx.Cell(exRow, 2).Value = v.TryGetValue("VALPAC__007", out var a) ? a ?? "" : "";
                    wsEx.Cell(exRow, 3).Value = v.TryGetValue("VALPAC__008", out var b) ? b ?? "" : "";
                    wsEx.Cell(exRow, 4).Value = v.TryGetValue("VALPAC__001", out var c2) ? c2 ?? "" : "";
                    wsEx.Cell(exRow, 5).Value = v.TryGetValue("VALPAC_COLYEAR", out var d) ? d ?? "" : "";
                    wsEx.Cell(exRow, 6).Value = ""; wsEx.Cell(exRow, 7).Value = ""; wsEx.Cell(exRow, 8).Value = ""; wsEx.Cell(exRow, 9).Value = "";
                    wsEx.Cell(exRow, 10).Value = "FAIL";
                    wsEx.Range(exRow, 1, exRow, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3F3");
                    exRow++;
                }
                for (int c = 1; c <= 10; c++) wsEx.Column(c).AdjustToContents();
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule51Csv(Rule51ValidationSummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Validation_Number,VALPAC__007,VALPAC__008,VALPAC__001,VALPAC_COLYEAR,PROD_IAGSTNO,PROD_IADIDNO,PROD_IAGQUAL,PROD_IAGCYR,Validation_Result");
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                sb.AppendLine(string.Join(",", new[]
                {
                    CsvEscape(row.ValidationNumber.ToString()),
                    CsvEscape(v.TryGetValue("VALPAC__007", out var a) ? a : ""),
                    CsvEscape(v.TryGetValue("VALPAC__008", out var b) ? b : ""),
                    CsvEscape(v.TryGetValue("VALPAC__001", out var c) ? c : ""),
                    CsvEscape(v.TryGetValue("VALPAC_COLYEAR", out var d) ? d : ""),
                    CsvEscape(v.TryGetValue("PROD_IAGSTNO", out var e) ? e : ""),
                    CsvEscape(v.TryGetValue("PROD_IADIDNO", out var f) ? f : ""),
                    CsvEscape(v.TryGetValue("PROD_IAGQUAL", out var g) ? g : ""),
                    CsvEscape(v.TryGetValue("PROD_IAGCYR", out var h) ? h : ""),
                    CsvEscape(row.ValidationResult)
                }));
            }
            return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── Rule 52: QUAL VALPAC Data in Production (same structure as Rule 51) ──

        public byte[] ExportRule52Excel(Rule52ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var wsResults = wb.Worksheets.Add("Validation Results");
            var vSubj = summary.ValpacSubjCol ?? "_001";
            var pSubj = summary.ProdSubjCol ?? "IAIQUAL";
            StyleHeaderRow(wsResults, 1, $"RULE 52: {summary.ValpacTable ?? "dbo_QUAL"} [{vSubj}] IN {summary.ProdTable ?? "MT-audit-prod-QUAL"} [{pSubj}]", 3);
            var headers = new[] { "Validation #", $"{summary.ValpacTable} [{vSubj}]", $"{summary.ProdTable} [{pSubj}]", "Result" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsResults.Cell(2, i + 1);
                cell.Value = headers[i]; cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }
            int rowIndex = 3;
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                wsResults.Cell(rowIndex, 1).Value = row.ValidationNumber;
                wsResults.Cell(rowIndex, 2).Value = v.TryGetValue("VALPAC_SUBJ", out var vs) ? vs ?? "" : "";
                wsResults.Cell(rowIndex, 3).Value = v.TryGetValue("PROD_SUBJ",   out var ps) ? ps ?? "" : "";
                wsResults.Cell(rowIndex, 4).Value = row.ValidationResult;
                wsResults.Range(rowIndex, 1, rowIndex, 4).Style.Fill.BackgroundColor =
                    string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase) ? XLColor.FromHtml("#FFF3F3") : XLColor.FromHtml("#F3FFF3");
                rowIndex++;
            }
            for (int c = 1; c <= 4; c++) wsResults.Column(c).AdjustToContents();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 52: dbo_QUAL QUALIFICATION CODE IN MT-audit-prod-QUAL", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database), ("Source Table (dbo_QUAL)", summary.ValpacTable), ("Target Table (MT-audit-prod-QUAL)", summary.ProdTable),
                ("Column Mapping", summary.TableLinkageText), ("Validation Date", summary.Timestamp), ("", ""),
                ("VALIDATION RESULTS", ""), ("Total Tested", summary.TotalValidated.ToString("N0")),
                ("PASS (found in PRODUCTION)", summary.PassCount.ToString("N0")), ("FAIL (not found in PRODUCTION)", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"), ("Status", summary.Status)
            };
            int sRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                { var hc = wsSummary.Cell(sRow, 1); hc.Value = label; hc.Style.Font.Bold = true; hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000"); hc.Style.Font.FontColor = XLColor.White; wsSummary.Range(sRow, 1, sRow, 2).Merge(); }
                else if (label != "")
                {
                    wsSummary.Cell(sRow, 1).Value = label; wsSummary.Cell(sRow, 1).Style.Font.Bold = true; wsSummary.Cell(sRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sRow, 2).Value = value;
                    if (label == "Status") { wsSummary.Cell(sRow, 2).Style.Fill.BackgroundColor = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2"); wsSummary.Cell(sRow, 2).Style.Font.Bold = true; }
                }
                sRow++;
            }
            wsSummary.Column(1).Width = 36; wsSummary.Column(2).Width = 70;
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule52Csv(Rule52ValidationSummary summary)
        {
            var vSubj = summary.ValpacSubjCol ?? "_001";
            var pSubj = summary.ProdSubjCol ?? "IAIQUAL";
            var sb = new StringBuilder();
            sb.AppendLine($"Validation_Number,VALPAC_{vSubj},PROD_{pSubj},Validation_Result");
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                sb.AppendLine(string.Join(",", new[]
                {
                    CsvEscape(row.ValidationNumber.ToString()),
                    CsvEscape(v.TryGetValue("VALPAC_SUBJ", out var a) ? a : ""),
                    CsvEscape(v.TryGetValue("PROD_SUBJ",   out var b) ? b : ""),
                    CsvEscape(row.ValidationResult)
                }));
            }
            return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── Rule 53: CRSE VALPAC Data in Production ──────────────────────────

        public byte[] ExportRule53Excel(Rule53ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var wsResults = wb.Worksheets.Add("Validation Results");
            var vSubj = summary.ValpacSubjCol ?? "_030";
            var pSubj = summary.ProdSubjCol ?? "IALSUBJ";
            StyleHeaderRow(wsResults, 1, $"RULE 53: {summary.ValpacTable ?? "dbo_CRSE"} [{vSubj}] IN {summary.ProdTable ?? "MT-audit-prod-CRSE"} [{pSubj}]", 3);
            var headers = new[] { "Validation #", $"{summary.ValpacTable} [{vSubj}]", $"{summary.ProdTable} [{pSubj}]", "Result" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = wsResults.Cell(2, i + 1);
                cell.Value = headers[i]; cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B5E20");
                cell.Style.Font.FontColor = XLColor.White;
            }
            int rowIndex = 3;
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                wsResults.Cell(rowIndex, 1).Value = row.ValidationNumber;
                wsResults.Cell(rowIndex, 2).Value = v.TryGetValue("VALPAC_SUBJ", out var vs) ? vs ?? "" : "";
                wsResults.Cell(rowIndex, 3).Value = v.TryGetValue("PROD_SUBJ",   out var ps) ? ps ?? "" : "";
                wsResults.Cell(rowIndex, 4).Value = row.ValidationResult;
                wsResults.Range(rowIndex, 1, rowIndex, 4).Style.Fill.BackgroundColor =
                    string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase) ? XLColor.FromHtml("#FFF3F3") : XLColor.FromHtml("#F3FFF3");
                rowIndex++;
            }
            for (int c = 1; c <= 4; c++) wsResults.Column(c).AdjustToContents();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 53: dbo_CRSE SUBJECT CODE IN MT-audit-prod-CRSE", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database), ("Source Table (dbo_CRSE)", summary.ValpacTable), ("Target Table (MT-audit-prod-CRSE)", summary.ProdTable),
                ("Column Mapping", summary.TableLinkageText), ("Validation Date", summary.Timestamp), ("", ""),
                ("VALIDATION RESULTS", ""), ("Total Tested", summary.TotalValidated.ToString("N0")),
                ("PASS (found in PRODUCTION)", summary.PassCount.ToString("N0")), ("FAIL (not found in PRODUCTION)", summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"), ("Status", summary.Status)
            };
            int sRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label == "VALIDATION RESULTS")
                { var hc = wsSummary.Cell(sRow, 1); hc.Value = label; hc.Style.Font.Bold = true; hc.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B5E20"); hc.Style.Font.FontColor = XLColor.White; wsSummary.Range(sRow, 1, sRow, 2).Merge(); }
                else if (label != "")
                {
                    wsSummary.Cell(sRow, 1).Value = label; wsSummary.Cell(sRow, 1).Style.Font.Bold = true; wsSummary.Cell(sRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sRow, 2).Value = value;
                    if (label == "Status") { wsSummary.Cell(sRow, 2).Style.Fill.BackgroundColor = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2"); wsSummary.Cell(sRow, 2).Style.Font.Bold = true; }
                }
                sRow++;
            }
            wsSummary.Column(1).Width = 36; wsSummary.Column(2).Width = 70;
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule53Csv(Rule53ValidationSummary summary)
        {
            var vSubj = summary.ValpacSubjCol ?? "_030";
            var pSubj = summary.ProdSubjCol ?? "IALSUBJ";
            var sb = new StringBuilder();
            sb.AppendLine($"Validation_Number,VALPAC_{vSubj},PROD_{pSubj},Validation_Result");
            foreach (var row in summary.ReviewRows)
            {
                var v = row.DisplayValues;
                sb.AppendLine(string.Join(",", new[]
                {
                    CsvEscape(row.ValidationNumber.ToString()),
                    CsvEscape(v.TryGetValue("VALPAC_SUBJ", out var a) ? a : ""),
                    CsvEscape(v.TryGetValue("PROD_SUBJ",   out var b) ? b : ""),
                    CsvEscape(row.ValidationResult)
                }));
            }
            return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── Rule 11: QUAL vs CESM vs PQM Validation ───────────────────────────

        public byte[] ExportRule11Excel(Rule11ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var qualHdr  = XLColor.FromHtml("#558B2F");
            var qualSub  = XLColor.FromHtml("#AED581");
            var cesmHdr  = XLColor.FromHtml("#00838F");
            var cesmSub  = XLColor.FromHtml("#80DEEA");
            var pqmHdr   = XLColor.FromHtml("#6A1B9A");
            var pqmSub   = XLColor.FromHtml("#CE93D8");
            var passRow  = XLColor.FromHtml("#F3FFF3");
            var failRow  = XLColor.FromHtml("#FFF3F3");
            var manualBg = XLColor.FromHtml("#FFFDE7");

            static void WpCell(IXLWorksheet ws, int r, int c, object? val,
                XLColor? bg = null, bool bold = false, bool italic = false,
                bool wrapText = false, XLAlignmentHorizontalValues halign = XLAlignmentHorizontalValues.Left)
            {
                var cell = ws.Cell(r, c);
                if (val != null) cell.Value = XLCellValue.FromObject(val);
                if (bg != null) cell.Style.Fill.BackgroundColor = bg;
                cell.Style.Font.Bold = bold;
                cell.Style.Font.Italic = italic;
                cell.Style.Alignment.WrapText = wrapText;
                cell.Style.Alignment.Horizontal = halign;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            static void SectionBanner(IXLWorksheet ws, int row, int cols, string text)
            {
                ws.Range(row, 1, row, cols).Merge();
                WpCell(ws, row, 1, text, XLColor.FromHtml("#6A1B9A"), bold: true,
                    halign: XLAlignmentHorizontalValues.Left);
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
                ws.Row(row).Height = 20;
            }

            // Column layout (17 cols): #|QUAL×9|CESM×2|PQM×3|Results|TM-a
            const int TOTAL_COLS = 17;

            void BuildHeader(IXLWorksheet ws, int hRow)
            {
                ws.Range(hRow, 1, hRow + 1, 1).Merge();
                WpCell(ws, hRow, 1, "#", XLColor.FromHtml("#424242"), bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 1).Style.Font.FontColor = XLColor.White;

                // QUAL cols 2-10
                ws.Range(hRow, 2, hRow, 10).Merge();
                WpCell(ws, hRow, 2, "dbo_QUAL", qualHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 2).Style.Font.FontColor = XLColor.White;

                // CESM cols 11-12
                ws.Range(hRow, 11, hRow, 12).Merge();
                WpCell(ws, hRow, 11, "dbo_CESM", cesmHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 11).Style.Font.FontColor = XLColor.White;

                // PQM cols 13-15
                ws.Range(hRow, 13, hRow, 15).Merge();
                WpCell(ws, hRow, 13, "PQM", pqmHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 13).Style.Font.FontColor = XLColor.White;

                // Results col 16 (merges 2 rows)
                ws.Range(hRow, 16, hRow + 1, 16).Merge();
                WpCell(ws, hRow, 16, "Results", XLColor.FromHtml("#37474F"), bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 16).Style.Font.FontColor = XLColor.White;

                // Tickmarks col 17 (label on row hRow, subheader on row hRow+1)
                WpCell(ws, hRow, 17, "Tickmark", XLColor.FromHtml("#4A148C"), bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 17).Style.Font.FontColor = XLColor.White;

                // Sub-headers
                foreach (var (col, lbl, bg) in new (int, string, XLColor)[]
                {
                    (2,  "_001", qualSub), (3, "_003", qualSub), (4, "_004", qualSub), (5, "_005", qualSub),
                    (6,  "_053", qualSub), (7, "_054", qualSub), (8, "_084", qualSub), (9, "_090", qualSub),
                    (10, "Population_Type", qualSub),
                    (11, "_001", cesmSub), (12, "_006", cesmSub),
                    (13, "Authorised_Qualification_Name", pqmSub),
                    (14, "HEQF_Qual_Type", pqmSub),
                    (15, "CESM_Code", pqmSub),
                    (17, "a", XLColor.FromHtml("#7B1FA2"))
                })
                {
                    WpCell(ws, hRow + 1, col, lbl, bg, bold: true, wrapText: true,
                        halign: XLAlignmentHorizontalValues.Center);
                    if (col == 17) ws.Cell(hRow + 1, col).Style.Font.FontColor = XLColor.White;
                }

                ws.Row(hRow).Height     = 22;
                ws.Row(hRow + 1).Height = 46;
            }

            void SetColWidths(IXLWorksheet ws)
            {
                ws.Column(1).Width  = 6;
                ws.Column(2).Width  = 14;
                ws.Column(3).Width  = 42;
                ws.Column(4).Width  = 10;
                ws.Column(5).Width  = 12;
                ws.Column(6).Width  = 14;
                ws.Column(7).Width  = 14;
                ws.Column(8).Width  = 14;
                ws.Column(9).Width  = 14;
                ws.Column(10).Width = 18;
                ws.Column(11).Width = 14;
                ws.Column(12).Width = 14;
                ws.Column(13).Width = 42;
                ws.Column(14).Width = 18;
                ws.Column(15).Width = 14;
                ws.Column(16).Width = 10;
                ws.Column(17).Width = 10;
            }

            void WriteDataRow(IXLWorksheet ws, int r,
                int no, string qualId, string qualName, string qualApproval, string qualHeqfType,
                string qual053, string qual054, string qual084, string qual090,
                string populationType,
                string? cesmCode,
                string? pqmName, string? pqmHeqfType, string? pqmCode,
                string result, bool isPass)
            {
                var rowBg   = isPass ? passRow : failRow;
                var tmColor = XLColor.FromHtml("#4A148C");

                WpCell(ws, r,  1, no,           rowBg, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r,  2, qualId,        rowBg);
                WpCell(ws, r,  3, qualName,      rowBg, wrapText: true);
                WpCell(ws, r,  4, qualApproval,  rowBg);
                WpCell(ws, r,  5, qualHeqfType,  rowBg);
                WpCell(ws, r,  6, qual053,      rowBg);
                WpCell(ws, r,  7, qual054,      rowBg);
                WpCell(ws, r,  8, qual084,      rowBg);
                WpCell(ws, r,  9, qual090,      rowBg);
                WpCell(ws, r, 10, populationType, rowBg);
                WpCell(ws, r, 11, qualId,        rowBg);
                WpCell(ws, r, 12, cesmCode ?? "", rowBg);

                if (!string.IsNullOrEmpty(pqmName))
                    WpCell(ws, r, 13, pqmName, rowBg, wrapText: true);
                else
                    WpCell(ws, r, 13, null, manualBg);

                if (!string.IsNullOrEmpty(pqmHeqfType))
                    WpCell(ws, r, 14, pqmHeqfType, rowBg);
                else
                    WpCell(ws, r, 14, null, manualBg);

                WpCell(ws, r, 15, pqmCode ?? "", rowBg);

                WpCell(ws, r, 16, result, rowBg, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(r, 16).Style.Font.FontColor =
                    isPass ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");

                if (isPass)
                {
                    WpCell(ws, r, 17, "a", passRow, bold: true, halign: XLAlignmentHorizontalValues.Center);
                    ws.Cell(r, 17).Style.Font.FontColor = tmColor;
                }
                else
                {
                    WpCell(ws, r, 17, null, failRow);
                }

                ws.Row(r).Height = 18;
            }

            void AppendProceduresAndTickmarks(IXLWorksheet ws, int startRow)
            {
                int r = startRow + 1;
                r++;
                SectionBanner(ws, r, TOTAL_COLS, "Procedures");
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "a.\tQUAL._003 qualification name agreed to PQM Authorised_Qualification_Name.",
                    XLColor.FromHtml("#F3E5F5"), wrapText: true);
                ws.Row(r).Height = 18;
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "b.\tQUAL._005 HEQF qualification type agreed to PQM HEQF_Qual_Type per EL §5.1.2.",
                    XLColor.FromHtml("#F3E5F5"), wrapText: true);
                ws.Row(r).Height = 18;
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "c.\tPopulation_Type is derived from QUAL._005 using the configured postgraduate type codes. Non-matching rows are labelled Undergraduate.",
                    XLColor.FromHtml("#F3E5F5"), wrapText: true);
                ws.Row(r).Height = 18;
                r++;
                r++;
                SectionBanner(ws, r, TOTAL_COLS, "Tickmark");
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "a\tAgreed qualification name to PQM. b = Agreed HEQF type to PQM. c = Undergraduate/Postgraduate label derived from QUAL._005.",
                    XLColor.FromHtml("#F3E5F5"), wrapText: true);
                ws.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#4A148C");
                ws.Cell(r, 1).Style.Font.Bold = true;
                ws.Row(r).Height = 18;
            }

            // ── Sheet 1: Working Paper ────────────────────────────────────────
            var wsResults = wb.Worksheets.Add("Working Paper");
            wsResults.Range(1, 1, 1, TOTAL_COLS).Merge();
            WpCell(wsResults, 1, 1, "HEMIS RULE 11 — QUAL vs CESM vs PQM VALIDATION",
                XLColor.FromHtml("#1A237E"), bold: true, halign: XLAlignmentHorizontalValues.Center);
            wsResults.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsResults.Cell(1, 1).Style.Font.FontSize  = 13;
            wsResults.Row(1).Height = 24;

            BuildHeader(wsResults, 2);
            SetColWidths(wsResults);

            int rowIdx = 4;
            foreach (var row in summary.ValidationRows)
            {
                WriteDataRow(wsResults, rowIdx,
                    row.ValidationNumber,
                    row.QualId, row.QualName, row.QualApproval, row.QualHeqfType,
                    row.Qual053, row.Qual054, row.Qual084, row.Qual090,
                    ResolveRule11PopulationType(summary, row.PopulationType, row.QualHeqfType),
                    row.CesmCode,
                    row.PqmName, row.PqmHeqfType, row.PqmCode,
                    row.ValidationResult, row.ValidationResult == "PASS");
                rowIdx++;
            }
            AppendProceduresAndTickmarks(wsResults, rowIdx);

            // ── Sheet 2: Exceptions ───────────────────────────────────────────
            var wsEx = wb.Worksheets.Add("Exceptions");
            wsEx.Range(1, 1, 1, TOTAL_COLS + 1).Merge();
            WpCell(wsEx, 1, 1, "HEMIS RULE 11 — EXCEPTIONS (QUAL vs CESM vs PQM)",
                XLColor.FromHtml("#B71C1C"), bold: true, halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsEx.Cell(1, 1).Style.Font.FontSize  = 13;
            wsEx.Row(1).Height = 24;

            BuildHeader(wsEx, 2);
            wsEx.Range(2, 18, 3, 18).Merge();
            WpCell(wsEx, 2, 18, "Exception Reason", XLColor.FromHtml("#B71C1C"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(2, 18).Style.Font.FontColor = XLColor.White;
            SetColWidths(wsEx);
            wsEx.Column(18).Width = 55;

            int exRow = 4;
            foreach (var ex in summary.Exceptions)
            {
                WriteDataRow(wsEx, exRow,
                    ex.ValidationNumber,
                    ex.QualId, ex.QualName, ex.QualApproval, ex.QualHeqfType,
                    ex.Qual053, ex.Qual054, ex.Qual084, ex.Qual090,
                    ResolveRule11PopulationType(summary, ex.PopulationType, ex.QualHeqfType),
                    ex.CesmCode,
                    ex.PqmName, ex.PqmHeqfType, ex.PqmCode,
                    ex.ValidationResult, false);
                WpCell(wsEx, exRow, 18, ex.ExceptionReason, failRow, wrapText: true);
                wsEx.Row(exRow).Height = 18;
                exRow++;
            }
            AppendProceduresAndTickmarks(wsEx, exRow);

            // ── Sheet 3: Summary ──────────────────────────────────────────────
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 11: QUAL vs CESM vs PQM VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database",           summary.Database),
                ("QUAL Table",         $"{summary.QualTable} (ID: {summary.QualIdCol}, Name: {summary.QualNameCol}, Approval: {summary.QualApprovalCol}, HEQF Type: {summary.QualHeqfTypeCol})"),
                ("QUAL Display Columns", "_001, _003, _004, _005, _053, _054, _084, _090, Population Type"),
                ("CESM Table",         $"{summary.CesmTable} (ID: {summary.CesmIdCol}, CESM Code: {summary.CesmCodeCol})"),
                ("PQM Table",          $"{summary.PqmTable} (Name: {summary.PqmNameCol}, HEQF Type: {summary.PqmHeqfTypeCol}, Code: {summary.PqmCodeCol})"),
                ("Population Classification", $"QUAL rows matching approval are validated; QUAL.{summary.QualHeqfTypeCol} values in the configured postgraduate list are labelled Postgraduate, otherwise Undergraduate."),
                ("Validation Date",    summary.Timestamp),
                ("",                   ""),
                ("VALIDATION RESULTS", ""),
                ("Total Validated",    summary.TotalValidated.ToString("N0")),
                ("PASS (Both matched)", summary.PassCount.ToString("N0")),
                ("FAIL (Mismatch)",    summary.FailCount.ToString("N0")),
                ("Exception Rate",     $"{summary.ExceptionRate:F2}%"),
                ("Status",             summary.Status),
                ("",                   ""),
                ("MATCHING RULES",     ""),
                ("Name Check",         $"QUAL.{summary.QualNameCol} (case-insensitive, whitespace-normalised) must match PQM.{summary.PqmNameCol}"),
                ("HEQF Type Check",    $"QUAL.{summary.QualHeqfTypeCol} must match PQM.{summary.PqmHeqfTypeCol} on the same PQM row"),
                ("EL Reference",       "EL §5.1.2 — Inspect E005 Qualification Type and agree correct type has been allocated per PQM.")
            };

            int summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label is "VALIDATION RESULTS" or "MATCHING RULES")
                {
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                    WpCell(wsSummary, summaryRow, 1, label, XLColor.FromHtml("#1A237E"), bold: true);
                    wsSummary.Cell(summaryRow, 1).Style.Font.FontColor = XLColor.White;
                }
                else if (label != "")
                {
                    WpCell(wsSummary, summaryRow, 1, label, XLColor.FromHtml("#F5F5F5"), bold: true);
                    WpCell(wsSummary, summaryRow, 2, value);
                    if (label == "Status")
                    {
                        var statusBg = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = statusBg;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }
                summaryRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 80;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule11Csv(Rule11ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            const string header = "Validation_Number,Qual_ID,Qual_Name,Qual_Approval,Qual_HEQF_Type,Qual_053,Qual_054,Qual_084,Qual_090,Population_Type,CESM_Code,PQM_Qual_Name,PQM_HEQF_Type,PQM_Code,Name_Match,HEQF_Type_Match,Validation_Result,Exception_Reason";
            sb.AppendLine(header);

            if (exceptionsOnly)
            {
                foreach (var ex in summary.Exceptions)
                {
                    sb.AppendLine(string.Join(",",
                        ex.ValidationNumber,
                        CsvEscape(ex.QualId),
                        CsvEscape(ex.QualName),
                        CsvEscape(ex.QualApproval),
                        CsvEscape(ex.QualHeqfType),
                        CsvEscape(ex.Qual053),
                        CsvEscape(ex.Qual054),
                        CsvEscape(ex.Qual084),
                        CsvEscape(ex.Qual090),
                        CsvEscape(ResolveRule11PopulationType(summary, ex.PopulationType, ex.QualHeqfType)),
                        CsvEscape(ex.CesmCode),
                        CsvEscape(ex.PqmName),
                        CsvEscape(ex.PqmHeqfType),
                        CsvEscape(ex.PqmCode),
                        ex.NameMatch ? "YES" : "NO",
                        ex.HeqfTypeMatch ? "YES" : "NO",
                        ex.ValidationResult,
                        CsvEscape(ex.ExceptionReason)));
                }
            }
            else
            {
                foreach (var row in summary.ValidationRows)
                {
                    sb.AppendLine(string.Join(",",
                        row.ValidationNumber,
                        CsvEscape(row.QualId),
                        CsvEscape(row.QualName),
                        CsvEscape(row.QualApproval),
                        CsvEscape(row.QualHeqfType),
                        CsvEscape(row.Qual053),
                        CsvEscape(row.Qual054),
                        CsvEscape(row.Qual084),
                        CsvEscape(row.Qual090),
                        CsvEscape(ResolveRule11PopulationType(summary, row.PopulationType, row.QualHeqfType)),
                        CsvEscape(row.CesmCode),
                        CsvEscape(row.PqmName),
                        CsvEscape(row.PqmHeqfType),
                        CsvEscape(row.PqmCode),
                        row.NameMatch ? "YES" : "NO",
                        row.HeqfTypeMatch ? "YES" : "NO",
                        row.ValidationResult,
                        CsvEscape(row.ExceptionReason)));
                }
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── Rule 54: CRED vs QUAL vs PQM Validation ───────────────────────────

        public byte[] ExportRule54Excel(Rule54ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            // ── colour palette matching the spreadsheet ────────────────────────
            var credHdr    = XLColor.FromHtml("#558B2F");
            var credSub    = XLColor.FromHtml("#AED581");
            var qualHdr    = XLColor.FromHtml("#00838F");
            var qualSub    = XLColor.FromHtml("#80DEEA");
            var pqmHdr     = XLColor.FromHtml("#6A1B9A");
            var pqmSub     = XLColor.FromHtml("#CE93D8");
            var passRow    = XLColor.FromHtml("#F3FFF3");
            var failRow    = XLColor.FromHtml("#FFF3F3");
            var manualBg   = XLColor.FromHtml("#FFFDE7"); // soft yellow = needs manual fill

            // ── local helpers ─────────────────────────────────────────────────
            static void WpCell(IXLWorksheet ws, int r, int c, object? val,
                XLColor? bg = null, bool bold = false, bool italic = false,
                bool wrapText = false, XLAlignmentHorizontalValues halign = XLAlignmentHorizontalValues.Left)
            {
                var cell = ws.Cell(r, c);
                if (val != null) cell.Value = XLCellValue.FromObject(val);
                if (bg != null) cell.Style.Fill.BackgroundColor = bg;
                cell.Style.Font.Bold = bold;
                cell.Style.Font.Italic = italic;
                cell.Style.Alignment.WrapText = wrapText;
                cell.Style.Alignment.Horizontal = halign;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            static void SectionBanner(IXLWorksheet ws, int row, int cols, string text)
            {
                ws.Range(row, 1, row, cols).Merge();
                WpCell(ws, row, 1, text, XLColor.FromHtml("#6A1B9A"), bold: true,
                    halign: XLAlignmentHorizontalValues.Left);
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
                ws.Row(row).Height = 20;
            }

            // ── column layout (13 cols): #|CRED×4|QUAL×2|PQM×3|Results|TM-a|TM-b
            const int TOTAL_COLS = 13;

            void BuildWorkingPaperHeader(IXLWorksheet ws, int hRow)
            {
                // Row hRow  = group labels
                // Row hRow+1 = sub-column labels

                // col 1 = # (merges 2 rows)
                ws.Range(hRow, 1, hRow + 1, 1).Merge();
                WpCell(ws, hRow, 1, "#", XLColor.FromHtml("#424242"), bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 1).Style.Font.FontColor = XLColor.White;

                // CRED group cols 2-5
                ws.Range(hRow, 2, hRow, 5).Merge();
                WpCell(ws, hRow, 2, "dbo_CRED", credHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 2).Style.Font.FontColor = XLColor.White;

                // QUAL group cols 6-7
                ws.Range(hRow, 6, hRow, 7).Merge();
                WpCell(ws, hRow, 6, "dbo_QUAL", qualHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 6).Style.Font.FontColor = XLColor.White;

                // PQM group cols 8-10
                ws.Range(hRow, 8, hRow, 10).Merge();
                WpCell(ws, hRow, 8, "Details as per PQM / Prospectus", pqmHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 8).Style.Font.FontColor = XLColor.White;

                // Results col 11 (merges 2 rows)
                ws.Range(hRow, 11, hRow + 1, 11).Merge();
                WpCell(ws, hRow, 11, "Results", XLColor.FromHtml("#37474F"), bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 11).Style.Font.FontColor = XLColor.White;

                // Tickmarks group cols 12-13
                ws.Range(hRow, 12, hRow, 13).Merge();
                WpCell(ws, hRow, 12, "Tickmarks", XLColor.FromHtml("#4A148C"), bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 12).Style.Font.FontColor = XLColor.White;

                // Sub-headers row hRow+1
                foreach (var (col, lbl, bg) in new (int, string, XLColor)[]
                {
                    (2,  "_001", credSub), (3, "_030", credSub), (4, "_036", credSub), (5, "_050", credSub),
                    (6,  "_001", qualSub), (7, "_003", qualSub),
                    (8,  "Qualification_name_Designator\n(Authorised_Qualification_Name)", pqmSub),
                    (9,  "Course_credit_value Recalculation\nE.g Credit/Total credits from Prospectus in the PDF", pqmSub),
                    (10, "Completed_research_course_credit_value\n(Research_1)", pqmSub),
                    (12, "a", XLColor.FromHtml("#7B1FA2")),
                    (13, "b", XLColor.FromHtml("#7B1FA2"))
                })
                {
                    WpCell(ws, hRow + 1, col, lbl, bg, bold: true, wrapText: true,
                        halign: XLAlignmentHorizontalValues.Center);
                    if (col is 12 or 13) ws.Cell(hRow + 1, col).Style.Font.FontColor = XLColor.White;
                }

                ws.Row(hRow).Height     = 22;
                ws.Row(hRow + 1).Height = 46;
            }

            void SetColWidths(IXLWorksheet ws)
            {
                ws.Column(1).Width  = 6;
                ws.Column(2).Width  = 14;
                ws.Column(3).Width  = 14;
                ws.Column(4).Width  = 12;
                ws.Column(5).Width  = 10;
                ws.Column(6).Width  = 14;
                ws.Column(7).Width  = 38;
                ws.Column(8).Width  = 42;
                ws.Column(9).Width  = 42;
                ws.Column(10).Width = 42;
                ws.Column(11).Width = 10;
                ws.Column(12).Width = 8;
                ws.Column(13).Width = 8;
            }

            void WriteDataRow(IXLWorksheet ws, int r,
                int no, string credId, string credCourse, string credCredit, string credRes1,
                string qualId, string qualName,
                string? pqmName, string? pqmRes1,
                string result, bool isPass)
            {
                var rowBg   = isPass ? passRow : failRow;
                var tmColor = XLColor.FromHtml("#4A148C");

                WpCell(ws, r,  1, no,         rowBg, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r,  2, credId,      rowBg);
                WpCell(ws, r,  3, credCourse,  rowBg);
                WpCell(ws, r,  4, credCredit,  rowBg);
                WpCell(ws, r,  5, credRes1,    rowBg);
                WpCell(ws, r,  6, qualId,      rowBg);
                WpCell(ws, r,  7, qualName,    rowBg, wrapText: true);

                // PQM Qual Name — show value if matched, else leave blank (manual fill)
                if (!string.IsNullOrEmpty(pqmName))
                    WpCell(ws, r, 8, pqmName, rowBg, wrapText: true);
                else
                    WpCell(ws, r, 8, null, manualBg);

                // Credit Recalc — always manual
                WpCell(ws, r, 9, null, manualBg);

                // PQM Research_1 — show value if matched, else leave blank (manual fill)
                if (!string.IsNullOrEmpty(pqmRes1))
                    WpCell(ws, r, 10, pqmRes1, rowBg);
                else
                    WpCell(ws, r, 10, null, manualBg);

                // Results
                WpCell(ws, r, 11, result, rowBg, bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(r, 11).Style.Font.FontColor =
                    isPass ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");

                // Tickmarks a / b — pre-filled for PASS rows, blank for FAIL
                if (isPass)
                {
                    WpCell(ws, r, 12, "a", passRow, bold: true, halign: XLAlignmentHorizontalValues.Center);
                    WpCell(ws, r, 13, "b", passRow, bold: true, halign: XLAlignmentHorizontalValues.Center);
                    ws.Cell(r, 12).Style.Font.FontColor = tmColor;
                    ws.Cell(r, 13).Style.Font.FontColor = tmColor;
                }
                else
                {
                    WpCell(ws, r, 12, null, failRow);
                    WpCell(ws, r, 13, null, failRow);
                }

                ws.Row(r).Height = 18;
            }

            void AppendProceduresAndTickmarks(IXLWorksheet ws, int startRow)
            {
                int r = startRow + 1; // blank spacer
                r++;

                SectionBanner(ws, r, TOTAL_COLS, "Procedures");
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "a.\tCourse credit value assigned agrees with the credit value defined in the PQM/Prospectus,",
                    XLColor.FromHtml("#F3E5F5"), wrapText: true);
                ws.Row(r).Height = 18;
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "b.\tCompleted research course credit value agrees with the fractional time for completed research courses in the PQM/Prospectus.",
                    XLColor.FromHtml("#F3E5F5"), wrapText: true);
                ws.Row(r).Height = 18;
                r++;

                r++; // blank spacer

                SectionBanner(ws, r, TOTAL_COLS, "Tickmark");
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "a\tAgreed the completed credit value as per the detail extract to the prospectus.",
                    XLColor.FromHtml("#F3E5F5"), wrapText: true);
                ws.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#4A148C");
                ws.Cell(r, 1).Style.Font.Bold = true;
                ws.Row(r).Height = 18;
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "b\tAgreed the Completed research course credit value with the fractional time for completed research courses in the PQM/Prospectus.",
                    XLColor.FromHtml("#F3E5F5"), wrapText: true);
                ws.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#4A148C");
                ws.Cell(r, 1).Style.Font.Bold = true;
                ws.Row(r).Height = 18;
            }

            // ── Sheet 1: Working Paper ────────────────────────────────────────
            var wsResults = wb.Worksheets.Add("Working Paper");

            wsResults.Range(1, 1, 1, TOTAL_COLS).Merge();
            WpCell(wsResults, 1, 1, "HEMIS RULE 54 — CRED vs QUAL vs PQM VALIDATION",
                XLColor.FromHtml("#1A237E"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsResults.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsResults.Cell(1, 1).Style.Font.FontSize  = 13;
            wsResults.Row(1).Height = 24;

            BuildWorkingPaperHeader(wsResults, 2);
            SetColWidths(wsResults);

            int rowIdx = 4;
            foreach (var row in summary.ValidationRows)
            {
                WriteDataRow(wsResults, rowIdx,
                    row.ValidationNumber,
                    row.RecordId, row.CourseCode, row.CreditValue, row.HemisResearch1,
                    row.QualRecordId.Length > 0 ? row.QualRecordId : row.RecordId, row.HemisQualName,
                    row.PqmQualName, row.PqmResearch1,
                    row.ValidationResult, row.ValidationResult == "PASS");
                rowIdx++;
            }

            AppendProceduresAndTickmarks(wsResults, rowIdx);

            // ── Sheet 2: Exceptions ───────────────────────────────────────────
            var wsEx = wb.Worksheets.Add("Exceptions");

            wsEx.Range(1, 1, 1, TOTAL_COLS + 1).Merge();
            WpCell(wsEx, 1, 1, "HEMIS RULE 54 — EXCEPTIONS (CRED vs QUAL vs PQM)",
                XLColor.FromHtml("#B71C1C"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsEx.Cell(1, 1).Style.Font.FontSize  = 13;
            wsEx.Row(1).Height = 24;

            BuildWorkingPaperHeader(wsEx, 2);
            // Add Exception Reason column (col 14)
            wsEx.Range(2, 14, 3, 14).Merge();
            WpCell(wsEx, 2, 14, "Exception Reason", XLColor.FromHtml("#B71C1C"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(2, 14).Style.Font.FontColor = XLColor.White;

            SetColWidths(wsEx);
            wsEx.Column(14).Width = 55;

            int exRow = 4;
            foreach (var ex in summary.Exceptions)
            {
                WriteDataRow(wsEx, exRow,
                    ex.ValidationNumber,
                    ex.RecordId, ex.CourseCode, ex.CreditValue, ex.HemisResearch1,
                    ex.QualRecordId.Length > 0 ? ex.QualRecordId : ex.RecordId, ex.HemisQualName,
                    ex.PqmQualName, ex.PqmResearch1,
                    ex.ValidationResult, false);
                WpCell(wsEx, exRow, 14, ex.ExceptionReason, failRow, wrapText: true);
                wsEx.Row(exRow).Height = 18;
                exRow++;
            }

            AppendProceduresAndTickmarks(wsEx, exRow);

            // ── Sheet 3: Summary ──────────────────────────────────────────────
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 54: CRED vs QUAL vs PQM VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database",           summary.Database),
                ("CRED Table",         $"{summary.CredTable} (ID: {summary.CredIdCol}, Course: {summary.CredCourseCol}, Credit: {summary.CredCreditCol}, Research_1: {summary.CredResearch1Col})"),
                ("QUAL Table",         $"{summary.QualTable} (ID: {summary.QualIdCol}, Name: {summary.QualNameCol})"),
                ("PQM Table",          $"{summary.PqmTable} (Name: {summary.PqmNameCol}, Research_1: {summary.PqmResearch1Col})"),
                ("Validation Date",    summary.Timestamp),
                ("",                   ""),
                ("VALIDATION RESULTS", ""),
                ("Total Validated",    summary.TotalValidated.ToString("N0")),
                ("PASS (Both matched)", summary.PassCount.ToString("N0")),
                ("FAIL (Mismatch)",    summary.FailCount.ToString("N0")),
                ("Exception Rate",     $"{summary.ExceptionRate:F2}%"),
                ("Status",             summary.Status),
                ("",                   ""),
                ("MATCHING RULES",     ""),
                ("Qual Name Check",    $"QUAL.{summary.QualNameCol} (case-insensitive) must match PQM.{summary.PqmNameCol}"),
                ("Research_1 Check",   $"CRED.{summary.CredResearch1Col} must match PQM.{summary.PqmResearch1Col} on the same PQM row"),
                ("Credit Value (a)",   $"CRED.{summary.CredCreditCol} — manual verification against PDF prospectus required (Tickmark a)"),
                ("Research_1 (b)",     $"CRED.{summary.CredResearch1Col} vs PQM.{summary.PqmResearch1Col} — auto-validated (Tickmark b)")
            };

            int summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label is "VALIDATION RESULTS" or "MATCHING RULES")
                {
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                    WpCell(wsSummary, summaryRow, 1, label, XLColor.FromHtml("#1A237E"), bold: true);
                    wsSummary.Cell(summaryRow, 1).Style.Font.FontColor = XLColor.White;
                }
                else if (label != "")
                {
                    WpCell(wsSummary, summaryRow, 1, label, XLColor.FromHtml("#F5F5F5"), bold: true);
                    WpCell(wsSummary, summaryRow, 2, value);
                    if (label == "Status")
                    {
                        var statusBg = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = statusBg;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }
                summaryRow++;
            }
            wsSummary.Column(1).Width = 30;
            wsSummary.Column(2).Width = 80;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule54Csv(Rule54ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            const string header = "Validation_Number,CRED_001,CRED_030,CRED_036,CRED_050,QUAL_001,QUAL_003,PQM_Authorised_Qual_Name,PQM_Research_1,Validation_Result,Exception_Reason";

            sb.AppendLine(header);

            if (exceptionsOnly)
            {
                foreach (var ex in summary.Exceptions)
                {
                    sb.AppendLine(string.Join(",",
                        ex.ValidationNumber,
                        CsvEscape(ex.RecordId),
                        CsvEscape(ex.CourseCode),
                        CsvEscape(ex.CreditValue),
                        CsvEscape(ex.HemisResearch1),
                        CsvEscape(ex.QualRecordId.Length > 0 ? ex.QualRecordId : ex.RecordId),
                        CsvEscape(ex.HemisQualName),
                        CsvEscape(ex.PqmQualName),
                        CsvEscape(ex.PqmResearch1),
                        ex.ValidationResult,
                        CsvEscape(ex.ExceptionReason)));
                }
            }
            else
            {
                foreach (var row in summary.ValidationRows)
                {
                    sb.AppendLine(string.Join(",",
                        row.ValidationNumber,
                        CsvEscape(row.RecordId),
                        CsvEscape(row.CourseCode),
                        CsvEscape(row.CreditValue),
                        CsvEscape(row.HemisResearch1),
                        CsvEscape(row.QualRecordId.Length > 0 ? row.QualRecordId : row.RecordId),
                        CsvEscape(row.HemisQualName),
                        CsvEscape(row.PqmQualName),
                        CsvEscape(row.PqmResearch1),
                        row.ValidationResult,
                        CsvEscape(row.ExceptionReason)));
                }
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        // ─── Rule 55: Graduate W-Code Validation ─────────────────────────────

        public byte[] ExportRule55Excel(Rule55ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var studHdr  = XLColor.FromHtml("#558B2F");
            var studSub  = XLColor.FromHtml("#AED581");
            var qualHdr  = XLColor.FromHtml("#00838F");
            var qualSub  = XLColor.FromHtml("#80DEEA");
            var passRow  = XLColor.FromHtml("#F3FFF3");
            var failRow  = XLColor.FromHtml("#FFF3F3");
            var manualBg = XLColor.FromHtml("#FFFDE7");

            static void WpCell(IXLWorksheet ws, int r, int c, object? val,
                XLColor? bg = null, bool bold = false, bool italic = false,
                bool wrapText = false, XLAlignmentHorizontalValues halign = XLAlignmentHorizontalValues.Left)
            {
                var cell = ws.Cell(r, c);
                if (val != null) cell.Value = XLCellValue.FromObject(val);
                if (bg != null) cell.Style.Fill.BackgroundColor = bg;
                cell.Style.Font.Bold = bold;
                cell.Style.Font.Italic = italic;
                cell.Style.Alignment.WrapText = wrapText;
                cell.Style.Alignment.Horizontal = halign;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            static void SectionBanner(IXLWorksheet ws, int row, int cols, string text)
            {
                ws.Range(row, 1, row, cols).Merge();
                WpCell(ws, row, 1, text, XLColor.FromHtml("#00838F"), bold: true,
                    halign: XLAlignmentHorizontalValues.Left);
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
                ws.Row(row).Height = 20;
            }

            // col layout (10 cols): # | STUD×3 | QUAL×4 | Results | TM-a
            const int TOTAL_COLS = 10;

            void BuildHeader(IXLWorksheet ws, int hRow)
            {
                // # col merges 2 rows
                ws.Range(hRow, 1, hRow + 1, 1).Merge();
                WpCell(ws, hRow, 1, "#", XLColor.FromHtml("#424242"), bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 1).Style.Font.FontColor = XLColor.White;

                // STUD group cols 2-4
                ws.Range(hRow, 2, hRow, 4).Merge();
                WpCell(ws, hRow, 2, "dbo_STUD", studHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 2).Style.Font.FontColor = XLColor.White;

                // QUAL group cols 5-8
                ws.Range(hRow, 5, hRow, 8).Merge();
                WpCell(ws, hRow, 5, "dbo_QUAL", qualHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 5).Style.Font.FontColor = XLColor.White;

                // Results col 9 merges 2 rows
                ws.Range(hRow, 9, hRow + 1, 9).Merge();
                WpCell(ws, hRow, 9, "Results", XLColor.FromHtml("#37474F"), bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 9).Style.Font.FontColor = XLColor.White;

                // Tickmarks col 10 merges 2 rows
                ws.Range(hRow, 10, hRow + 1, 10).Merge();
                WpCell(ws, hRow, 10, "Tickmarks", XLColor.FromHtml("#4A148C"), bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 10).Style.Font.FontColor = XLColor.White;

                // Sub-headers
                foreach (var (col, lbl, bg) in new (int, string, XLColor)[]
                {
                    (2, "Student_ID (_007)", studSub),
                    (3, "Qual_Code (_001)", studSub),
                    (4, "_025 Value", studSub),
                    (5, "Qual_Code (_001)", qualSub),
                    (6, "Qual_Name (_003)", qualSub),
                    (7, "Qual_Type (_005)", qualSub),
                    (8, "Approval (_004)", qualSub)
                })
                {
                    WpCell(ws, hRow + 1, col, lbl, bg, bold: true, wrapText: true,
                        halign: XLAlignmentHorizontalValues.Center);
                }

                ws.Row(hRow).Height     = 22;
                ws.Row(hRow + 1).Height = 40;
            }

            void SetColWidths(IXLWorksheet ws)
            {
                ws.Column(1).Width  = 6;
                ws.Column(2).Width  = 18;
                ws.Column(3).Width  = 14;
                ws.Column(4).Width  = 12;
                ws.Column(5).Width  = 14;
                ws.Column(6).Width  = 42;
                ws.Column(7).Width  = 20;
                ws.Column(8).Width  = 14;
                ws.Column(9).Width  = 10;
                ws.Column(10).Width = 8;
            }

            void WriteDataRow(IXLWorksheet ws, int r,
                int no, string studentId, string qualCode, string fulfilledStatus,
                string? qualName, string? qualType, string? qualApproval,
                string result, bool isPass)
            {
                var rowBg   = isPass ? passRow : failRow;
                var tmColor = XLColor.FromHtml("#4A148C");

                WpCell(ws, r,  1, no,              rowBg, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r,  2, studentId,        rowBg);
                WpCell(ws, r,  3, qualCode,          rowBg);
                WpCell(ws, r,  4, fulfilledStatus,   rowBg);

                // QUAL cols — null means not found in dbo_QUAL → manualBg
                if (qualName != null)
                    WpCell(ws, r, 5, qualCode, rowBg);
                else
                    WpCell(ws, r, 5, null, manualBg);

                if (qualName != null)
                    WpCell(ws, r, 6, qualName, rowBg, wrapText: true);
                else
                    WpCell(ws, r, 6, null, manualBg);

                if (qualType != null)
                    WpCell(ws, r, 7, qualType, rowBg);
                else
                    WpCell(ws, r, 7, null, manualBg);

                if (qualApproval != null)
                    WpCell(ws, r, 8, qualApproval, rowBg);
                else
                    WpCell(ws, r, 8, null, manualBg);

                WpCell(ws, r, 9, result, rowBg, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(r, 9).Style.Font.FontColor =
                    isPass ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");

                if (isPass)
                {
                    WpCell(ws, r, 10, "a", passRow, bold: true, halign: XLAlignmentHorizontalValues.Center);
                    ws.Cell(r, 10).Style.Font.FontColor = tmColor;
                }
                else
                {
                    WpCell(ws, r, 10, null, failRow);
                }

                ws.Row(r).Height = 18;
            }

            void AppendProceduresAndTickmarks(IXLWorksheet ws, int startRow)
            {
                int r = startRow + 1;
                r++;

                SectionBanner(ws, r, TOTAL_COLS, "Procedures");
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "a.\tAgreed student W-coded qualification to dbo_QUAL approved register (_004='A') per DHET §1.5.",
                    XLColor.FromHtml("#E0F7FA"), wrapText: true);
                ws.Row(r).Height = 18;
                r++;

                r++;

                SectionBanner(ws, r, TOTAL_COLS, "Tickmark");
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "a\tAgreed the W-coded student's qualification as approved for state funding in the dbo_QUAL register.",
                    XLColor.FromHtml("#E0F7FA"), wrapText: true);
                ws.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#4A148C");
                ws.Cell(r, 1).Style.Font.Bold = true;
                ws.Row(r).Height = 18;
            }

            // ── Sheet 1: Working Paper ────────────────────────────────────────
            var wsResults = wb.Worksheets.Add("Working Paper");

            wsResults.Range(1, 1, 1, TOTAL_COLS).Merge();
            WpCell(wsResults, 1, 1, "HEMIS RULE 55 — GRADUATE W-CODE VALIDATION",
                XLColor.FromHtml("#1A237E"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsResults.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsResults.Cell(1, 1).Style.Font.FontSize  = 13;
            wsResults.Row(1).Height = 24;

            BuildHeader(wsResults, 2);
            SetColWidths(wsResults);

            int rowIdx = 4;
            foreach (var row in summary.ValidationRows)
            {
                WriteDataRow(wsResults, rowIdx,
                    row.ValidationNumber,
                    row.StudentId, row.QualCode, row.FulfilledStatus,
                    row.QualName, row.QualType, row.QualApprovalStatus,
                    row.ValidationResult, row.ValidationResult == "PASS");
                rowIdx++;
            }

            AppendProceduresAndTickmarks(wsResults, rowIdx);

            // ── Sheet 2: Exceptions ───────────────────────────────────────────
            var wsEx = wb.Worksheets.Add("Exceptions");

            wsEx.Range(1, 1, 1, TOTAL_COLS + 1).Merge();
            WpCell(wsEx, 1, 1, "HEMIS RULE 55 — EXCEPTIONS (W-CODE VALIDATION)",
                XLColor.FromHtml("#B71C1C"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsEx.Cell(1, 1).Style.Font.FontSize  = 13;
            wsEx.Row(1).Height = 24;

            BuildHeader(wsEx, 2);
            // Exception Reason col 11
            wsEx.Range(2, 11, 3, 11).Merge();
            WpCell(wsEx, 2, 11, "Exception Reason", XLColor.FromHtml("#B71C1C"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(2, 11).Style.Font.FontColor = XLColor.White;

            SetColWidths(wsEx);
            wsEx.Column(11).Width = 55;

            int exRow = 4;
            foreach (var ex in summary.Exceptions)
            {
                WriteDataRow(wsEx, exRow,
                    ex.ValidationNumber,
                    ex.StudentId, ex.QualCode, ex.FulfilledStatus,
                    ex.QualName, ex.QualType, ex.QualApprovalStatus,
                    ex.ValidationResult, false);
                WpCell(wsEx, exRow, 11, ex.ExceptionReason, failRow, wrapText: true);
                wsEx.Row(exRow).Height = 18;
                exRow++;
            }

            AppendProceduresAndTickmarks(wsEx, exRow);

            // ── Sheet 3: Summary ──────────────────────────────────────────────
            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 55: GRADUATE W-CODE VALIDATION", 2);
            var summaryData = new[]
            {
                ("Database",         summary.Database),
                ("STUD Table",       $"{summary.StudTable} (ID: {summary.StudIdCol}, Qual Code: {summary.StudQualCodeCol}, Fulfilled: {summary.StudFulfilledCol})"),
                ("QUAL Table",       $"{summary.QualTable} (Code: {summary.QualCodeCol}, Name: {summary.QualNameCol}, Type: {summary.QualTypeCol}, Approval: {summary.QualApprovalCol})"),
                ("Validation Date",  summary.Timestamp),
                ("",                 ""),
                ("VALIDATION RESULTS", ""),
                ("W-Coded Students (pop)", summary.TotalValidated.ToString("N0")),
                ("PASS (Qual & Approved)", summary.PassCount.ToString("N0")),
                ("FAIL (Exception)",       summary.FailCount.ToString("N0")),
                ("Exception Rate",         $"{summary.ExceptionRate:F2}%"),
                ("Status",                 summary.Status),
                ("",                       ""),
                ("VALIDATION RULE",        ""),
                ("Population",             $"dbo_STUD WHERE {summary.StudFulfilledCol} = 'W' (certificate withheld)"),
                ("Join",                   $"LEFT JOIN {summary.QualTable} on {summary.StudQualCodeCol} = {summary.QualCodeCol}"),
                ("PASS Condition",         $"QUAL row found AND {summary.QualApprovalCol} = 'A' (approved for state funding)"),
                ("DHET Reference",         "DHET §1.5: Element 025='W' graduates treated identically to 'F' graduates")
            };

            int summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label is "VALIDATION RESULTS" or "VALIDATION RULE")
                {
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                    WpCell(wsSummary, summaryRow, 1, label, XLColor.FromHtml("#00838F"), bold: true);
                    wsSummary.Cell(summaryRow, 1).Style.Font.FontColor = XLColor.White;
                }
                else if (label != "")
                {
                    WpCell(wsSummary, summaryRow, 1, label, XLColor.FromHtml("#F5F5F5"), bold: true);
                    WpCell(wsSummary, summaryRow, 2, value);
                    if (label == "Status")
                    {
                        var statusBg = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = statusBg;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }
                summaryRow++;
            }
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 80;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule55Csv(Rule55ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            const string header = "Validation_Number,Student_ID,Qual_Code,Fulfilled_Status,Qual_Name,Qual_Type,Qual_Approval_Status,Validation_Result,Exception_Reason";

            sb.AppendLine(header);

            if (exceptionsOnly)
            {
                foreach (var ex in summary.Exceptions)
                {
                    sb.AppendLine(string.Join(",",
                        ex.ValidationNumber,
                        CsvEscape(ex.StudentId),
                        CsvEscape(ex.QualCode),
                        CsvEscape(ex.FulfilledStatus),
                        CsvEscape(ex.QualName),
                        CsvEscape(ex.QualType),
                        CsvEscape(ex.QualApprovalStatus),
                        ex.ValidationResult,
                        CsvEscape(ex.ExceptionReason)));
                }
            }
            else
            {
                foreach (var row in summary.ValidationRows)
                {
                    sb.AppendLine(string.Join(",",
                        row.ValidationNumber,
                        CsvEscape(row.StudentId),
                        CsvEscape(row.QualCode),
                        CsvEscape(row.FulfilledStatus),
                        CsvEscape(row.QualName),
                        CsvEscape(row.QualType),
                        CsvEscape(row.QualApprovalStatus),
                        row.ValidationResult,
                        CsvEscape(row.ExceptionReason)));
                }
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportRule57Excel(Rule57ValidationSummary summary)
        {
            using var wb = new XLWorkbook();

            var studHdr = XLColor.FromHtml("#1565C0");
            var studSub = XLColor.FromHtml("#90CAF9");
            var cregHdr = XLColor.FromHtml("#E65100");
            var cregSub = XLColor.FromHtml("#FFCC80");
            var passRow = XLColor.FromHtml("#F3FFF3");
            var failRow = XLColor.FromHtml("#FFF3F3");

            static void WpCell(IXLWorksheet ws, int r, int c, object? val,
                XLColor? bg = null, bool bold = false, bool italic = false,
                bool wrapText = false, XLAlignmentHorizontalValues halign = XLAlignmentHorizontalValues.Left)
            {
                var cell = ws.Cell(r, c);
                if (val != null) cell.Value = XLCellValue.FromObject(val);
                if (bg != null) cell.Style.Fill.BackgroundColor = bg;
                cell.Style.Font.Bold = bold;
                cell.Style.Font.Italic = italic;
                cell.Style.Alignment.WrapText = wrapText;
                cell.Style.Alignment.Horizontal = halign;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            static void SectionBanner(IXLWorksheet ws, int row, int cols, string text)
            {
                ws.Range(row, 1, row, cols).Merge();
                WpCell(ws, row, 1, text, XLColor.FromHtml("#1565C0"), bold: true,
                    halign: XLAlignmentHorizontalValues.Left);
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
                ws.Row(row).Height = 20;
            }

            const int TOTAL_COLS = 8;

            void BuildHeader(IXLWorksheet ws, int hRow)
            {
                ws.Range(hRow, 1, hRow + 1, 1).Merge();
                WpCell(ws, hRow, 1, "#", XLColor.FromHtml("#424242"), bold: true,
                    halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 1).Style.Font.FontColor = XLColor.White;

                ws.Range(hRow, 2, hRow, 4).Merge();
                WpCell(ws, hRow, 2, "dbo_CREG", cregHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 2).Style.Font.FontColor = XLColor.White;

                ws.Range(hRow, 5, hRow, 6).Merge();
                WpCell(ws, hRow, 5, "dbo_STUD", studHdr, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 5).Style.Font.FontColor = XLColor.White;

                ws.Range(hRow, 7, hRow + 1, 7).Merge();
                WpCell(ws, hRow, 7, "Results", XLColor.FromHtml("#37474F"), bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 7).Style.Font.FontColor = XLColor.White;

                ws.Range(hRow, 8, hRow + 1, 8).Merge();
                WpCell(ws, hRow, 8, "Tickmarks", XLColor.FromHtml("#4A148C"), bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(hRow, 8).Style.Font.FontColor = XLColor.White;

                foreach (var (col, lbl, bg) in new (int, string, XLColor)[]
                {
                    (2, "Student_ID (_007)", cregSub),
                    (3, "CREG_Code (_001)", cregSub),
                    (4, "Reg_Type (_064)", cregSub),
                    (5, "STUD_Code (_001)", studSub),
                    (6, "Reg_Type (_024)", studSub)
                })
                {
                    WpCell(ws, hRow + 1, col, lbl, bg, bold: true, wrapText: true,
                        halign: XLAlignmentHorizontalValues.Center);
                }

                ws.Row(hRow).Height     = 22;
                ws.Row(hRow + 1).Height = 40;
            }

            void SetColWidths(IXLWorksheet ws)
            {
                ws.Column(1).Width = 6;
                ws.Column(2).Width = 18;
                ws.Column(3).Width = 14;
                ws.Column(4).Width = 14;
                ws.Column(5).Width = 14;
                ws.Column(6).Width = 14;
                ws.Column(7).Width = 10;
                ws.Column(8).Width = 8;
            }

            void WriteDataRow(IXLWorksheet ws, int r,
                int no, string studentId, string? cregCode, string? cregRegType,
                string studCode, string studRegType,
                string result, bool isPass)
            {
                var rowBg   = isPass ? passRow : failRow;
                var tmColor = XLColor.FromHtml("#4A148C");

                WpCell(ws, r, 1, no,          rowBg, halign: XLAlignmentHorizontalValues.Center);
                WpCell(ws, r, 2, studentId,   rowBg);
                WpCell(ws, r, 3, cregCode,    rowBg);
                WpCell(ws, r, 4, cregRegType, rowBg);
                WpCell(ws, r, 5, studCode,    rowBg);
                WpCell(ws, r, 6, studRegType, rowBg);
                WpCell(ws, r, 7, result, rowBg, bold: true, halign: XLAlignmentHorizontalValues.Center);
                ws.Cell(r, 7).Style.Font.FontColor = isPass ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");

                if (isPass)
                {
                    WpCell(ws, r, 8, "a", passRow, bold: true, halign: XLAlignmentHorizontalValues.Center);
                    ws.Cell(r, 8).Style.Font.FontColor = tmColor;
                }
                else
                {
                    WpCell(ws, r, 8, null, failRow);
                }
                ws.Row(r).Height = 18;
            }

            void AppendProceduresAndTickmarks(IXLWorksheet ws, int startRow)
            {
                int r = startRow + 2;
                SectionBanner(ws, r, TOTAL_COLS, "Procedures");
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "a.\tAgreed the student's registration type (STUD._024) to the registration documentation (CREG._064) per the signed application.",
                    XLColor.FromHtml("#E3F2FD"), wrapText: true);
                ws.Row(r).Height = 18;
                r += 2;
                SectionBanner(ws, r, TOTAL_COLS, "Tickmark");
                r++;
                ws.Range(r, 1, r, TOTAL_COLS).Merge();
                WpCell(ws, r, 1,
                    "a\tAgreed STUD._024 = CREG._064 — registration type agrees with signed application and/or registration documentation.",
                    XLColor.FromHtml("#E3F2FD"), wrapText: true);
                ws.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#4A148C");
                ws.Cell(r, 1).Style.Font.Bold = true;
                ws.Row(r).Height = 18;
            }

            var wsResults = wb.Worksheets.Add("Working Paper");
            wsResults.Range(1, 1, 1, TOTAL_COLS).Merge();
            WpCell(wsResults, 1, 1, "HEMIS RULE 57 — REGISTRATION DOCUMENTATION AGREEMENT",
                XLColor.FromHtml("#1A237E"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsResults.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsResults.Cell(1, 1).Style.Font.FontSize  = 13;
            wsResults.Row(1).Height = 24;
            BuildHeader(wsResults, 2);
            SetColWidths(wsResults);

            int rowIdx = 4;
            foreach (var row in summary.ValidationRows)
            {
                WriteDataRow(wsResults, rowIdx,
                    row.ValidationNumber,
                    row.StudentId, row.CregCode, row.CregRegType,
                    row.StudCode, row.StudRegType,
                    row.ValidationResult, row.ValidationResult == "PASS");
                rowIdx++;
            }
            AppendProceduresAndTickmarks(wsResults, rowIdx);

            var wsEx = wb.Worksheets.Add("Exceptions");
            wsEx.Range(1, 1, 1, TOTAL_COLS + 1).Merge();
            WpCell(wsEx, 1, 1, "HEMIS RULE 57 — EXCEPTIONS (REGISTRATION DOCUMENTATION AGREEMENT)",
                XLColor.FromHtml("#B71C1C"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            wsEx.Cell(1, 1).Style.Font.FontSize  = 13;
            wsEx.Row(1).Height = 24;
            BuildHeader(wsEx, 2);
            wsEx.Range(2, TOTAL_COLS + 1, 3, TOTAL_COLS + 1).Merge();
            WpCell(wsEx, 2, TOTAL_COLS + 1, "Exception Reason", XLColor.FromHtml("#B71C1C"), bold: true,
                halign: XLAlignmentHorizontalValues.Center);
            wsEx.Cell(2, TOTAL_COLS + 1).Style.Font.FontColor = XLColor.White;
            SetColWidths(wsEx);
            wsEx.Column(TOTAL_COLS + 1).Width = 55;

            int exRow = 4;
            foreach (var ex in summary.Exceptions)
            {
                WriteDataRow(wsEx, exRow,
                    ex.ValidationNumber,
                    ex.StudentId, ex.CregCode, ex.CregRegType,
                    ex.StudCode, ex.StudRegType,
                    ex.ValidationResult, false);
                WpCell(wsEx, exRow, TOTAL_COLS + 1, ex.ExceptionReason, failRow, wrapText: true);
                wsEx.Row(exRow).Height = 18;
                exRow++;
            }
            AppendProceduresAndTickmarks(wsEx, exRow);

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 57: REGISTRATION DOCUMENTATION AGREEMENT", 2);
            var summaryData = new[]
            {
                ("Database",          summary.Database),
                ("STUD Table",        $"{summary.StudTable} (ID: {summary.StudIdCol}, Code: {summary.StudCodeCol}, Reg Type: {summary.StudRegTypeCol})"),
                ("CREG Table",        $"{summary.CregTable} (ID: {summary.CregIdCol}, Code: {summary.CregCodeCol}, Reg Type: {summary.CregRegTypeCol})"),
                ("CREG Filter (_064)", summary.CregRegTypeFilterValue),
                ("Validation Date",   summary.Timestamp),
                ("", ""),
                ("VALIDATION RESULTS", ""),
                ("Filtered Records",  summary.TotalValidated.ToString("N0")),
                ("PASS",              summary.PassCount.ToString("N0")),
                ("FAIL (Exception)",  summary.FailCount.ToString("N0")),
                ("Exception Rate",    $"{summary.ExceptionRate:F2}%"),
                ("Status",            summary.Status),
                ("", ""),
                ("VALIDATION RULE",   ""),
                ("Population",        $"dbo_CREG WHERE {summary.CregRegTypeCol} = '{summary.CregRegTypeFilterValue}'"),
                ("Join",              $"LEFT JOIN {summary.StudTable} on STUD.{summary.StudIdCol} = CREG.{summary.CregIdCol}"),
                ("PASS Condition",    $"STUD.{summary.StudRegTypeCol} = CREG.{summary.CregRegTypeCol}"),
                ("Description",       "Agrees to the student's signed application and/or registration documentation.")
            };

            int summaryRow = 2;
            foreach (var (label, value) in summaryData)
            {
                if (label is "VALIDATION RESULTS" or "VALIDATION RULE")
                {
                    wsSummary.Range(summaryRow, 1, summaryRow, 2).Merge();
                    WpCell(wsSummary, summaryRow, 1, label, XLColor.FromHtml("#1565C0"), bold: true);
                    wsSummary.Cell(summaryRow, 1).Style.Font.FontColor = XLColor.White;
                }
                else if (label != "")
                {
                    WpCell(wsSummary, summaryRow, 1, label, XLColor.FromHtml("#F5F5F5"), bold: true);
                    WpCell(wsSummary, summaryRow, 2, value);
                    if (label == "Status")
                    {
                        var statusBg = value == "PASS" ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(summaryRow, 2).Style.Fill.BackgroundColor = statusBg;
                        wsSummary.Cell(summaryRow, 2).Style.Font.Bold = true;
                    }
                }
                summaryRow++;
            }
            wsSummary.Column(1).Width = 32;
            wsSummary.Column(2).Width = 80;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule57Csv(Rule57ValidationSummary summary, bool exceptionsOnly = false)
        {
            var sb = new StringBuilder();
            const string header = "Validation_Number,Student_ID,CREG_Code,CREG_Reg_Type,STUD_Code,STUD_Reg_Type,Validation_Result,Exception_Reason";

            sb.AppendLine(header);

            if (exceptionsOnly)
            {
                foreach (var ex in summary.Exceptions)
                {
                    sb.AppendLine(string.Join(",",
                        ex.ValidationNumber,
                        CsvEscape(ex.StudentId),
                        CsvEscape(ex.CregCode),
                        CsvEscape(ex.CregRegType),
                        CsvEscape(ex.StudCode),
                        CsvEscape(ex.StudRegType),
                        ex.ValidationResult,
                        CsvEscape(ex.ExceptionReason)));
                }
            }
            else
            {
                foreach (var row in summary.ValidationRows)
                {
                    sb.AppendLine(string.Join(",",
                        row.ValidationNumber,
                        CsvEscape(row.StudentId),
                        CsvEscape(row.CregCode),
                        CsvEscape(row.CregRegType),
                        CsvEscape(row.StudCode),
                        CsvEscape(row.StudRegType),
                        row.ValidationResult,
                        CsvEscape(row.ExceptionReason)));
                }
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportRule58Excel(Rule58ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Working Paper");

            ws.Range(1, 1, 1, 6).Merge();
            var title = ws.Cell(1, 1);
            title.Value = "HEMIS RULE 58 — STAFF VALPAC DATA IN PRODUCTION";
            title.Style.Fill.BackgroundColor = XLColor.FromHtml("#1A237E");
            title.Style.Font.FontColor = XLColor.White;
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 13;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(1).Height = 24;

            string[] headers = { "#", "Control Type", "Control Label", "Result", "Explanation", "Display Values" };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(2, c + 1);
                cell.Value = headers[c];
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#37474F");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.WrapText = true;
            }
            ws.Row(2).Height = 22;

            var passRow = XLColor.FromHtml("#F3FFF3");
            var failRow = XLColor.FromHtml("#FFF3F3");

            int r = 3;
            foreach (var row in summary.ReviewRows)
            {
                bool isPass = row.ValidationResult == "PASS";
                var bg = isPass ? passRow : failRow;
                ws.Cell(r, 1).Value = row.ValidationNumber;
                ws.Cell(r, 2).Value = row.ControlType;
                ws.Cell(r, 3).Value = row.ControlLabel;
                ws.Cell(r, 4).Value = row.ValidationResult;
                ws.Cell(r, 5).Value = row.ValidationExplanation;
                ws.Cell(r, 6).Value = string.Join("; ", row.DisplayValues.Select(kv => $"{kv.Key}={kv.Value}"));
                for (int c = 1; c <= 6; c++)
                    ws.Cell(r, c).Style.Fill.BackgroundColor = bg;
                ws.Cell(r, 4).Style.Font.FontColor = isPass ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");
                ws.Cell(r, 4).Style.Font.Bold = true;
                ws.Row(r).Height = 18;
                r++;
            }

            ws.Column(1).Width = 6; ws.Column(2).Width = 18; ws.Column(3).Width = 24;
            ws.Column(4).Width = 10; ws.Column(5).Width = 50; ws.Column(6).Width = 40;

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 58: STAFF VALPAC DATA IN PRODUCTION", 2);
            var rows = new[]
            {
                ("Database",       summary.Database),
                ("VALPAC Table",   summary.ValpacTable),
                ("Production Table", summary.ProdTable),
                ("Validation Date", summary.Timestamp),
                ("",               ""),
                ("RESULTS",        ""),
                ("Total Validated", summary.TotalValidated.ToString("N0")),
                ("Pass",           summary.PassCount.ToString("N0")),
                ("Fail",           summary.FailCount.ToString("N0")),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status",         summary.Status)
            };
            int sr = 2;
            foreach (var (lbl, val) in rows)
            {
                if (lbl == "RESULTS")
                {
                    wsSummary.Range(sr, 1, sr, 2).Merge();
                    var hdr = wsSummary.Cell(sr, 1);
                    hdr.Value = lbl; hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#37474F");
                    hdr.Style.Font.FontColor = XLColor.White; hdr.Style.Font.Bold = true;
                }
                else if (lbl != "")
                {
                    wsSummary.Cell(sr, 1).Value = lbl;
                    wsSummary.Cell(sr, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sr, 1).Style.Font.Bold = true;
                    wsSummary.Cell(sr, 2).Value = val;
                    if (lbl == "Status")
                    {
                        wsSummary.Cell(sr, 2).Style.Fill.BackgroundColor = val == "PASS"
                            ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(sr, 2).Style.Font.Bold = true;
                    }
                }
                sr++;
            }
            wsSummary.Column(1).Width = 26; wsSummary.Column(2).Width = 60;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule58Csv(Rule58ValidationSummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Validation_Number,Control_Type,Control_Label,Validation_Result,Explanation");
            foreach (var row in summary.ReviewRows)
            {
                sb.AppendLine(string.Join(",",
                    row.ValidationNumber,
                    CsvEscape(row.ControlType),
                    CsvEscape(row.ControlLabel),
                    row.ValidationResult,
                    CsvEscape(row.ValidationExplanation)));
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public byte[] ExportRule59Excel(Rule59ValidationSummary summary)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Working Paper");

            ws.Range(1, 1, 1, 6).Merge();
            var title = ws.Cell(1, 1);
            title.Value = "HEMIS RULE 59 — SFTE VALPAC DATA IN STAFF PRODUCTION";
            title.Style.Fill.BackgroundColor = XLColor.FromHtml("#1A237E");
            title.Style.Font.FontColor = XLColor.White;
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 13;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(1).Height = 24;

            string[] headers = { "#", "Control Type", "Control Label", "Result", "Explanation", "Display Values" };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(2, c + 1);
                cell.Value = headers[c];
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#37474F");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.WrapText = true;
            }
            ws.Row(2).Height = 22;

            var passRow = XLColor.FromHtml("#F3FFF3");
            var failRow = XLColor.FromHtml("#FFF3F3");

            int r = 3;
            foreach (var row in summary.ReviewRows)
            {
                bool isPass = row.ValidationResult == "PASS";
                var bg = isPass ? passRow : failRow;
                ws.Cell(r, 1).Value = row.ValidationNumber;
                ws.Cell(r, 2).Value = row.ControlType;
                ws.Cell(r, 3).Value = row.ControlLabel;
                ws.Cell(r, 4).Value = row.ValidationResult;
                ws.Cell(r, 5).Value = row.ValidationExplanation;
                ws.Cell(r, 6).Value = string.Join("; ", row.DisplayValues.Select(kv => $"{kv.Key}={kv.Value}"));
                for (int c = 1; c <= 6; c++)
                    ws.Cell(r, c).Style.Fill.BackgroundColor = bg;
                ws.Cell(r, 4).Style.Font.FontColor = isPass ? XLColor.FromHtml("#1B5E20") : XLColor.FromHtml("#B71C1C");
                ws.Cell(r, 4).Style.Font.Bold = true;
                ws.Row(r).Height = 18;
                r++;
            }

            ws.Column(1).Width = 6; ws.Column(2).Width = 18; ws.Column(3).Width = 24;
            ws.Column(4).Width = 10; ws.Column(5).Width = 50; ws.Column(6).Width = 40;

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 59: SFTE VALPAC DATA IN STAFF PRODUCTION", 2);
            var rows = new[]
            {
                ("Database",         summary.Database),
                ("VALPAC Table",     summary.ValpacTable),
                ("Production Table", summary.ProdTable),
                ("Column Mapping",   summary.TableLinkageText),
                ("Validation Date",  summary.Timestamp),
                ("", ""),
                ("RESULTS",          ""),
                ("Total Validated",  summary.TotalValidated.ToString("N0")),
                ("Pass",             summary.PassCount.ToString("N0")),
                ("Fail",             summary.FailCount.ToString("N0")),
                ("Exception Rate",   $"{summary.ExceptionRate:F2}%"),
                ("Status",           summary.Status)
            };
            int sr = 2;
            foreach (var (lbl, val) in rows)
            {
                if (lbl == "RESULTS")
                {
                    wsSummary.Range(sr, 1, sr, 2).Merge();
                    var hdr = wsSummary.Cell(sr, 1);
                    hdr.Value = lbl; hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#37474F");
                    hdr.Style.Font.FontColor = XLColor.White; hdr.Style.Font.Bold = true;
                }
                else if (lbl != "")
                {
                    wsSummary.Cell(sr, 1).Value = lbl;
                    wsSummary.Cell(sr, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    wsSummary.Cell(sr, 1).Style.Font.Bold = true;
                    wsSummary.Cell(sr, 2).Value = val;
                    if (lbl == "Status")
                    {
                        wsSummary.Cell(sr, 2).Style.Fill.BackgroundColor = val == "PASS"
                            ? XLColor.FromHtml("#C8E6C9") : XLColor.FromHtml("#FFCDD2");
                        wsSummary.Cell(sr, 2).Style.Font.Bold = true;
                    }
                }
                sr++;
            }
            wsSummary.Column(1).Width = 26; wsSummary.Column(2).Width = 60;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportRule59Csv(Rule59ValidationSummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Validation_Number,Control_Type,Control_Label,Validation_Result,Explanation");
            foreach (var row in summary.ReviewRows)
            {
                sb.AppendLine(string.Join(",",
                    row.ValidationNumber,
                    CsvEscape(row.ControlType),
                    CsvEscape(row.ControlLabel),
                    row.ValidationResult,
                    CsvEscape(row.ValidationExplanation)));
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }
    }
}
