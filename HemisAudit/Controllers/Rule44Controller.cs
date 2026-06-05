using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;
using HemisAudit.Helpers;
using HemisAudit.Models;
using HemisAudit.Services;
using HemisAudit.ViewModels;

namespace HemisAudit.Controllers
{
    [Authorize]
    public class Rule44Controller : Controller
    {
        private readonly IRule44Service _rule44;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;
        private readonly ILogger<Rule44Controller> _logger;

        public Rule44Controller(
            IRule44Service rule44,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb,
            ILogger<Rule44Controller> logger)
        {
            _rule44   = rule44;
            _audit    = audit;
            _users    = users;
            _systemDb = systemDb;
            _logger   = logger;
        }

        public async Task<IActionResult> Index(int clientId = 0)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();

            if (!string.Equals(role, "Admin",       StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "Director",    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "Manager",     StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "Trainee",     StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Only assigned engagement members can open audit modules.";
                return RedirectToAction("Index", "Dashboard");
            }

            var clients = await _systemDb.GetClientsAsync(user, role, approvedOnly: true);
            if (clientId > 0 && !await _systemDb.CanAccessClientResultsAsync(clientId, user, role))
            {
                TempData["Error"] = "You cannot access this engagement.";
                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.Clients = clients
                .Select(c => new Client
                {
                    Id = c.Id, Name = c.EngagementName, FiscalYear = c.MaconomyNumber,
                    Status = c.Status, CreatedAt = c.CreatedAt, CreatedByUserId = "", IsActive = true
                })
                .ToList();
            ViewBag.ClientId          = clientId;
            ViewBag.CurrentSystemRole = role;
            ViewBag.ModuleNavigation  = ModuleSequenceNavigationHelper.BuildForWorkspace(44, clientId);
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetWorkspaceState(int clientId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();

            if (clientId <= 0) return Json(new { success = true, hasWorkspace = false });
            if (!await _systemDb.CanAccessClientResultsAsync(clientId, user, role))
            {
                Response.StatusCode = StatusCodes.Status403Forbidden;
                return Json(new { success = false, error = "You cannot access this engagement." });
            }

            var workspace      = await _rule44.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            if (workspace != null && !resultsVisible) workspace.Summary = null;

            return Json(new { success = true, hasWorkspace = workspace != null, resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule44.GetDatabasesAsync(model.Server, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule44.GetTablesAsync(model.Server, model.Database, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetColumns([FromBody] Rule44GetColumnsRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule44.GetColumnsAsync(request.Server, request.Database, request.Driver, request.TableName)));

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] Rule44VerifyRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule44.VerifyTablesAsync(request)));

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] Rule44ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new Rule44ValidationSummary { Success = false, Error = "Select an approved engagement before running validation." });

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule44ValidationSummary { Success = false, Error = "You cannot access this engagement." });

            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return Json(new Rule44ValidationSummary { Success = false, Error = "Only the assigned data analyst can run Rule 44." });

            var result = await _rule44.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);
            _logger.LogInformation("Rule44 completed for {Email}. Status={Status}, Total={Total}, Pass={Pass}, Fail={Fail}",
                user?.Email, result.Status, result.TotalCount, result.PassCount, result.FailCount);

            if (result.Success)
                await _audit.LogAsync("run_validation", $"Rule 44 on client {request.ClientId}: {result.Status} ({result.FailCount} fail rows), run {result.SavedRunId}", user?.Id, user?.Email);

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] Rule44ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!await CanEditAsync(request.ClientId, user, role))
                return Json(new Rule44WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can save a workspace." });

            var result = await _rule44.SaveWorkspaceAsync(request, user!.Email!, user.FullName);
            if (result.Success)
                await _audit.LogAsync("save_validation_workspace", $"DataAnalyst saved Rule 44 workspace for client {request.ClientId}.", user.Id, user.Email);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> BeginWorkspaceEdit([FromBody] Rule44ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!await CanEditAsync(request.ClientId, user, role))
                return Json(new Rule44WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can edit a saved workspace." });
            if (!request.RunId.HasValue || request.RunId.Value <= 0)
                return Json(new Rule44WorkspaceSaveResult { Success = false, Error = "Select a saved run before editing the workspace." });

            var result = await _rule44.BeginWorkspaceEditAsync(request.RunId.Value, user!.Email!, user.FullName);
            if (result.Success)
                await _audit.LogAsync("workspace_edit_started", $"DataAnalyst started editing Rule 44 run {request.RunId.Value}.", user.Id, user.Email);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSql([FromBody] Rule44ValidationRequest request) =>
            Json(new Rule44SqlResult { Success = true, Sql = _rule44.GenerateSql(request) });

        [HttpPost]
        public async Task<IActionResult> SignOffWorkspace([FromBody] Rule44WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0)
                return Json(new { success = false, error = "Select an engagement before signing off." });
            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can sign off." });
            if (!model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Run the validation first so the saved workspace exists." });

            var review = await _rule44.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved validation run could not be found for this engagement." });
            if (!review.IsCurrentRun)
                return Json(new { success = false, error = "History results are read-only. Signoff is only available on the current run." });
            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
                return Json(new { success = false, error = "The assigned data analyst must sign off before this review can be completed." });

            await _rule44.AddOrUpdateSignoffAsync(model.RunId.Value, user!.Email!, model.Comment);
            await _audit.LogAsync("signoff_validation_run", $"Rule 44 signoff saved for run {model.RunId.Value}", user.Id, user.Email);

            var workspace      = await _rule44.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            return Json(new { success = true, message = "Signoff saved.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveWorkspaceSignoff([FromBody] Rule44WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0 || !model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Select a saved run before removing signoff." });
            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can remove signoff." });

            var review = await _rule44.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved validation run could not be found for this engagement." });
            if (!review.IsCurrentRun)
                return Json(new { success = false, error = "History results are read-only." });
            if (!review.CurrentUserHasSignedOff)
                return Json(new { success = false, error = "There is no signoff for your assigned engagement role to remove." });

            await _rule44.RemoveSignoffAsync(model.RunId.Value, user!.Email!);
            await _audit.LogAsync("remove_validation_signoff", $"Signoff removed for Rule 44 run {model.RunId.Value}", user.Id, user.Email);

            var workspace      = await _rule44.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            return Json(new { success = true, message = "Signoff removed.", resultsVisible, workspace });
        }

        [HttpPost]
        public IActionResult DownloadExcel([FromBody] Rule44ValidationSummary summary)
        {
            if (summary == null)
                return BadRequest("Rule 44 summary payload is required.");
            var bytes = BuildExcelExport(summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Rule44_Research_Time_{Ts()}.xlsx");
        }

        [HttpPost]
        public IActionResult DownloadCsv([FromBody] Rule44ValidationSummary summary)
        {
            if (summary == null)
                return BadRequest("Rule 44 summary payload is required.");
            var bytes = BuildCsvExport(summary);
            return File(bytes, "text/csv", $"Rule44_Research_Time_{Ts()}.csv");
        }

        [HttpPost]
        public IActionResult DownloadSql([FromBody] Rule44ValidationRequest request)
        {
            var sql = _rule44.GenerateSql(request);
            var bytes = System.Text.Encoding.UTF8.GetBytes(sql);
            return File(bytes, "application/sql", $"Rule44_Research_Time_{Ts()}.sql");
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static string Ts() => DateTime.Now.ToString("yyyyMMdd_HHmmss");

        private static byte[] BuildExcelExport(Rule44ValidationSummary summary)
        {
            using var workbook = new XLWorkbook();

            var summarySheet = workbook.Worksheets.Add("Summary");
            summarySheet.Cell(1, 1).Value = "HEMIS RULE 44 - Masters/PhD Research Time Validation";
            summarySheet.Cell(2, 1).Value = "Database";
            summarySheet.Cell(2, 2).Value = summary.Database;
            summarySheet.Cell(3, 1).Value = "Timestamp";
            summarySheet.Cell(3, 2).Value = summary.Timestamp;
            summarySheet.Cell(4, 1).Value = "STUD Table";
            summarySheet.Cell(4, 2).Value = summary.StudTable;
            summarySheet.Cell(5, 1).Value = "QUAL Table";
            summarySheet.Cell(5, 2).Value = summary.QualTable;
            summarySheet.Cell(6, 1).Value = "PQM Table";
            summarySheet.Cell(6, 2).Value = summary.PqmTable;
            summarySheet.Cell(7, 1).Value = "PG Types";
            summarySheet.Cell(7, 2).Value = summary.PgTypesText;
            summarySheet.Cell(8, 1).Value = "Total";
            summarySheet.Cell(8, 2).Value = summary.TotalCount;
            summarySheet.Cell(8, 3).Value = "Pass";
            summarySheet.Cell(8, 4).Value = summary.PassCount;
            summarySheet.Cell(8, 5).Value = "Fail";
            summarySheet.Cell(8, 6).Value = summary.FailCount;
            summarySheet.Cell(8, 7).Value = "Missing PQM";
            summarySheet.Cell(8, 8).Value = summary.MissingPqmCount;
            summarySheet.Cell(8, 9).Value = "Exception Rate";
            summarySheet.Cell(8, 10).Value = $"{summary.ExceptionRate:0.00}%";
            summarySheet.Cell(1, 1).Style.Font.Bold = true;
            summarySheet.Cell(1, 1).Style.Font.FontSize = 14;
            foreach (var cellAddress in new[] { "A2", "A3", "A4", "A5", "A6", "A7", "A8", "C8", "E8", "G8", "I8" })
            {
                summarySheet.Cell(cellAddress).Style.Font.Bold = true;
            }
            summarySheet.Columns().AdjustToContents();

            WriteReviewWorksheet(workbook.Worksheets.Add("All Results"), summary.PassRows.Concat(summary.FailRows));
            WriteReviewWorksheet(workbook.Worksheets.Add("Pass Rows"), summary.PassRows);
            WriteReviewWorksheet(workbook.Worksheets.Add("Fail Rows"), summary.FailRows);

            using var ms = new System.IO.MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }

        private static byte[] BuildCsvExport(Rule44ValidationSummary summary)
        {
            using var ms = new System.IO.MemoryStream();
            using var sw = new System.IO.StreamWriter(ms, System.Text.Encoding.UTF8);
            sw.WriteLine("\"HEMIS RULE 44 – Research Time Validation\"");
            sw.WriteLine($"\"Database\",\"{summary.Database}\"");
            sw.WriteLine($"\"Timestamp\",\"{summary.Timestamp}\"");
            sw.WriteLine($"\"Status\",\"{summary.Status}\"");
            sw.WriteLine($"\"Total\",{summary.TotalCount},\"Pass\",{summary.PassCount},\"Fail\",{summary.FailCount}");
            sw.WriteLine();
            WriteReviewCsv(sw, summary, false);
            sw.Flush();
            return ms.ToArray();
        }

        private static void WriteReviewCsv(System.IO.StreamWriter sw, Rule44ValidationSummary summary, bool exceptionsOnly)
        {
            sw.WriteLine("\"Row\",\"Student No\",\"Student ID\",\"Qual Code\",\"Status\",\"Qual Name\",\"Qual Type\",\"STUD Research %\",\"PQM Research %\",\"Result\",\"Explanation\"");
            var rows = exceptionsOnly ? summary.FailRows : summary.PassRows.Concat(summary.FailRows);
            foreach (var r in rows)
            {
                sw.WriteLine($"{r.RowNumber},\"{r.StudentNo}\",\"{r.StudentId}\",\"{r.QualCode}\",\"{r.StudStatus}\",\"{r.QualName.Replace("\"", "\"\"")}\",\"{r.QualType}\",\"{r.StudResearchTime}\",\"{r.PqmResearchTime}\",\"{r.ValidationResult}\",\"{r.ValidationExplanation.Replace("\"", "\"\"")}\"");
            }
        }

        private static void WriteReviewWorksheet(IXLWorksheet worksheet, IEnumerable<Rule44ReviewRow> rows)
        {
            var headers = new[]
            {
                "Row",
                "Student No",
                "Student ID",
                "Qual Code",
                "Status",
                "Qual Name",
                "Qual Type",
                "STUD Research %",
                "PQM Research %",
                "Result",
                "Explanation"
            };

            for (var i = 0; i < headers.Length; i++)
            {
                var cell = worksheet.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");
            }

            var rowIndex = 2;
            foreach (var row in rows)
            {
                worksheet.Cell(rowIndex, 1).Value = row.RowNumber;
                worksheet.Cell(rowIndex, 2).Value = row.StudentNo;
                worksheet.Cell(rowIndex, 3).Value = row.StudentId;
                worksheet.Cell(rowIndex, 4).Value = row.QualCode;
                worksheet.Cell(rowIndex, 5).Value = row.StudStatus;
                worksheet.Cell(rowIndex, 6).Value = row.QualName;
                worksheet.Cell(rowIndex, 7).Value = row.QualType;
                worksheet.Cell(rowIndex, 8).Value = row.StudResearchTime;
                worksheet.Cell(rowIndex, 9).Value = row.PqmResearchTime;
                worksheet.Cell(rowIndex, 10).Value = row.ValidationResult;
                worksheet.Cell(rowIndex, 11).Value = row.ValidationExplanation;
                rowIndex++;
            }

            worksheet.Columns().AdjustToContents();
        }
        private async Task<string> GetCurrentSystemRoleAsync(ApplicationUser? user)
        {
            var systemRole = await _systemDb.GetSystemRoleAsync(user);
            if (!string.IsNullOrWhiteSpace(systemRole)) return systemRole!;
            var roles = user != null ? await _users.GetRolesAsync(user) : new List<string>();
            return roles.FirstOrDefault() ?? "";
        }

        private static bool CanViewResults(string role, Rule44WorkspaceStateViewModel? workspace)
        {
            if (workspace == null) return false;
            return ValidationRunAccessPolicy.CanViewSignedResults(role, workspace.CurrentUserEngagementRole, workspace.HasDataAnalystSignoff);
        }

        private async Task<bool> CanEditAsync(int clientId, ApplicationUser? user, string role)
        {
            if (user == null || !string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) || clientId <= 0) return false;
            if (!await _systemDb.CanAccessClientResultsAsync(clientId, user, role)) return false;
            var engagementRole = await _systemDb.GetEngagementRoleAsync(clientId, user, role);
            return ValidationRunAccessPolicy.IsAssignedDataAnalyst(engagementRole);
        }

        private async Task<object> RequireDataAnalystAsync<T>(Func<Task<T>> action)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return new { success = false, error = "Only the assigned data analyst can configure Rule 44." };
            return await action();
        }
    }
}
