using ClosedXML.Excel;
using System.Text;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IExportService
    {
        byte[] ExportExcel(ValidationSummary summary);
        byte[] ExportExcel(Rule34ValidationSummary summary);
        byte[] ExportExcel(Rule26ValidationSummary summary);
        byte[] ExportExcel(Rule27ValidationSummary summary);
        byte[] ExportExcel(Rule22ValidationSummary summary);
        byte[] ExportExcel(Rule23ValidationSummary summary);
        byte[] ExportExcel(Rule24ValidationSummary summary);
        byte[] ExportExcel(Rule25ValidationSummary summary);
        byte[] ExportExcel(Rule21ValidationSummary summary);
        byte[] ExportExcel(Rule29ValidationSummary summary);
        byte[] ExportExcel(Rule31ValidationSummary summary);
        byte[] ExportExcel(Rule32ValidationSummary summary);
        byte[] ExportCsv(ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportCsv(Rule34ValidationSummary summary, bool exceptionsOnly = false);
        byte[] ExportCsv(Rule26ValidationSummary summary);
        byte[] ExportCsv(Rule27ValidationSummary summary);
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
