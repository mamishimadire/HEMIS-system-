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

            var wsResults = wb.Worksheets.Add("Validation Results");
            StyleHeaderRow(wsResults, 1, "RULE 34 VALIDATION RESULTS", 10);
            var resultHeaders = new[]
            {
                "Validation Number",
                "First Day",
                "Last Day",
                "c_Days",
                "c_Days_2",
                "Prepared Census Date",
                "Actual Census Date",
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
                wsResults.Cell(rowIndex, 4).Value = row.CurrentDays?.ToString() ?? "NULL";
                wsResults.Cell(rowIndex, 5).Value = row.CurrentDaysHalf?.ToString() ?? "NULL";
                wsResults.Cell(rowIndex, 6).Value = row.ComputedCensusDate;
                wsResults.Cell(rowIndex, 7).Value = row.ActualCensusDate;
                wsResults.Cell(rowIndex, 8).Value = row.CensusDateValue;
                wsResults.Cell(rowIndex, 9).Value = row.DayStatus;
                wsResults.Cell(rowIndex, 10).Value = row.ValidationStatus;

                wsResults.Range(rowIndex, 1, rowIndex, 10).Style.Fill.BackgroundColor =
                    row.DateMatch ? XLColor.FromHtml("#F3FFF3") : XLColor.FromHtml("#FFF3F3");
                rowIndex++;
            }

            for (int c = 1; c <= 10; c++) wsResults.Column(c).AdjustToContents();

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
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 26: BI-DIRECTIONAL 5-CONTROL VALIDATION", 2);
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
                var sheetName = direction.DirectionKey == "prof_to_payroll" ? "PROF to Payroll" : "Payroll to PROF";
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

            var wsAll = wb.Worksheets.Add("All Results");
            StyleHeaderRow(wsAll, 1, "RULE 12 ALL RESULTS", 5);
            var headers = new[] { "Validation Number", "CREG._030", "CRSE._030", "Validation Result", "Validation Explanation" };
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = wsAll.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var rowIndex = 3;
            foreach (var row in summary.ReviewRows)
            {
                wsAll.Cell(rowIndex, 1).Value = row.ValidationNumber;
                wsAll.Cell(rowIndex, 2).Value = row.DisplayValues.TryGetValue("CREG__030", out var creg) ? creg : "";
                wsAll.Cell(rowIndex, 3).Value = row.DisplayValues.TryGetValue("CRSE__030", out var crse) ? crse : "";
                wsAll.Cell(rowIndex, 4).Value = row.ValidationResult;
                wsAll.Cell(rowIndex, 5).Value = row.ValidationExplanation;
                rowIndex++;
            }
            for (var c = 1; c <= 5; c++)
                wsAll.Column(c).AdjustToContents();

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
        {
            using var wb = new XLWorkbook();

            var wsSummary = wb.Worksheets.Add("Summary");
            StyleHeaderRow(wsSummary, 1, "HEMIS RULE 11: QUALIFICATION SELECTION FROM dbo_QUAL", 2);
            var summaryData = new[]
            {
                ("Database", summary.Database),
                ("QUAL Table", summary.QualTable),
                ("Exclude M Pattern", summary.ExcludeMPrefixPattern ? "Yes (M_____ excluded in Control 1)" : "No"),
                ("Postgraduate Types", summary.PostgraduateTypesCsv),
                ("Validation Date", summary.Timestamp),
                ("Rule Mode", summary.RuleModeText),
                ("", ""),
                ("RESULT SUMMARY", ""),
                ("QUAL Records", summary.QualRecordCount.ToString("N0")),
                ("Approved Qualifications", summary.ApprovedQualificationCount.ToString("N0")),
                ("Undergraduate Rows", summary.UndergraduateCount.ToString("N0")),
                ("Postgraduate Rows", summary.PostgraduateCount.ToString("N0")),
                ("Total Selected Rows", summary.TotalValidated.ToString("N0")),
                ("PASS Rows", summary.PassCount.ToString("N0")),
                ("FAIL Rows", summary.FailCount.ToString("N0")),
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
            wsSummary.Column(2).Width = 80;

            var wsControlSummary = wb.Worksheets.Add("Control Summary");
            StyleHeaderRow(wsControlSummary, 1, "RULE 11 CONTROL SUMMARY", 6);
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
            StyleHeaderRow(wsAll, 1, "RULE 11 ALL RESULTS", 8);
            var headers = new[] { "Validation Number", "Control", "QUAL._001", "QUAL._002", "QUAL._004", "QUAL._005", "Validation Result", "Validation Explanation" };
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = wsAll.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B0000");
                cell.Style.Font.FontColor = XLColor.White;
            }

            var rowIndex = 3;
            foreach (var row in summary.ReviewRows)
            {
                wsAll.Cell(rowIndex, 1).Value = row.ValidationNumber;
                wsAll.Cell(rowIndex, 2).Value = row.ControlLabel;
                wsAll.Cell(rowIndex, 3).Value = row.DisplayValues.TryGetValue("QUAL__001", out var qual001) ? qual001 : "";
                wsAll.Cell(rowIndex, 4).Value = row.DisplayValues.TryGetValue("QUAL__002", out var qual002) ? qual002 : "";
                wsAll.Cell(rowIndex, 5).Value = row.DisplayValues.TryGetValue("QUAL__004", out var qual004) ? qual004 : "";
                wsAll.Cell(rowIndex, 6).Value = row.DisplayValues.TryGetValue("QUAL__005", out var qual005) ? qual005 : "";
                wsAll.Cell(rowIndex, 7).Value = row.ValidationResult;
                wsAll.Cell(rowIndex, 8).Value = row.ValidationExplanation;
                rowIndex++;
            }
            for (var c = 1; c <= 8; c++)
                wsAll.Column(c).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

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
        {
            var sb = new StringBuilder();
            sb.AppendLine("Validation Number,Control,QUAL._001,QUAL._002,QUAL._004,QUAL._005,Validation Result,Validation Explanation");

            foreach (var row in summary.ReviewRows)
            {
                var qual001 = row.DisplayValues.TryGetValue("QUAL__001", out var qual001Value) ? qual001Value : null;
                var qual002 = row.DisplayValues.TryGetValue("QUAL__002", out var qual002Value) ? qual002Value : null;
                var qual004 = row.DisplayValues.TryGetValue("QUAL__004", out var qual004Value) ? qual004Value : null;
                var qual005 = row.DisplayValues.TryGetValue("QUAL__005", out var qual005Value) ? qual005Value : null;

                sb.AppendLine(string.Join(",",
                    row.ValidationNumber,
                    CsvEscape(row.ControlLabel),
                    CsvEscape(qual001),
                    CsvEscape(qual002),
                    CsvEscape(qual004),
                    CsvEscape(qual005),
                    CsvEscape(row.ValidationResult),
                    CsvEscape(row.ValidationExplanation)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

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
    }
}
