using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using HemisAudit.Helpers;
using HemisAudit.Models;
using HemisAudit.Services;
using HemisAudit.ViewModels;

namespace HemisAudit.Controllers
{
    [Authorize]
    public class Rule61Controller : Controller
    {
        private readonly IRule61Service _rule61;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;
        private readonly ILogger<Rule61Controller> _logger;

        public Rule61Controller(
            IRule61Service rule61,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb,
            ILogger<Rule61Controller> logger)
        {
            _rule61   = rule61;
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
            ViewBag.ModuleNavigation  = ModuleSequenceNavigationHelper.BuildForWorkspace(61, clientId);
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

            var workspace      = await _rule61.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            if (workspace != null && !resultsVisible) workspace.Summary = null;

            return Json(new { success = true, hasWorkspace = workspace != null, resultsVisible, workspace });
        }

        [HttpGet]
        public async Task<IActionResult> Run(int id)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();

            var review = await _rule61.GetSavedRunAsync(id, user?.Email);
            if (review == null)
                return NotFound();

            if (!await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
            {
                TempData["Error"] = "You do not have access to this saved validation run.";
                return RedirectToAction("Index", "Dashboard");
            }

            if (!ValidationRunAccessPolicy.CanViewSignedResults(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
            {
                TempData["Error"] = "Only analyst-signed validation results are available for review.";
                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.IsAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
            ViewBag.CanManageEngagement =
                string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);
            var clientDetail = await _systemDb.GetClientDetailAsync(review.ClientId, user, role);
            var isArchived = clientDetail?.IsArchived == true;
            ViewBag.IsArchived = isArchived;
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForSavedRun(
                61, review.ClientId, clientDetail?.ValidationRuns, role, review.CurrentUserEngagementRole);
            ViewBag.CanOpenWorkspace =
                !isArchived &&
                await _systemDb.CanAccessClientModuleAsync(review.ClientId, user, role) &&
                (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(review.CurrentUserEngagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            review.GeneratedSql = _rule61.GenerateSql(new Rule61ValidationRequest
            {
                ClientId = review.ClientId,
                Database = review.Summary.Database,
                StudTable = review.Summary.StudTable,
                QualTable = review.Summary.QualTable,
                PqmTable = review.Summary.PqmTable,
                PgTypesText = review.Summary.PgTypesText,
                ColumnMapping = review.Summary.ColumnMapping
            });

            return View(review);
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule61.GetDatabasesAsync(model.Server, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule61.GetTablesAsync(model.Server, model.Database, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetColumns([FromBody] Rule61GetColumnsRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule61.GetColumnsAsync(request.Server, request.Database, request.Driver, request.TableName)));

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] Rule61VerifyRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule61.VerifyTablesAsync(request)));

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] Rule61ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new Rule61ValidationSummary { Success = false, Error = "Select an approved engagement before running validation." });

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule61ValidationSummary { Success = false, Error = "You cannot access this engagement." });

            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return Json(new Rule61ValidationSummary { Success = false, Error = "Only the assigned data analyst can run Rule 61." });

            var result = await _rule61.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);
            _logger.LogInformation("Rule61 completed for {Email}. Status={Status}, Total={Total}, Pass={Pass}, Fail={Fail}",
                user?.Email, result.Status, result.TotalCount, result.PassCount, result.FailCount);

            if (result.Success)
                await _audit.LogAsync("run_validation", $"Rule 61 on client {request.ClientId}: {result.Status} ({result.FailCount} fail rows), run {result.SavedRunId}", user?.Id, user?.Email);

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] Rule61ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!await CanEditAsync(request.ClientId, user, role))
                return Json(new Rule61WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can save a workspace." });

            var result = await _rule61.SaveWorkspaceAsync(request, user!.Email!, user.FullName);
            if (result.Success)
                await _audit.LogAsync("save_validation_workspace", $"DataAnalyst saved Rule 61 workspace for client {request.ClientId}.", user.Id, user.Email);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> BeginWorkspaceEdit([FromBody] Rule61ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!await CanEditAsync(request.ClientId, user, role))
                return Json(new Rule61WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can edit a saved workspace." });
            if (!request.RunId.HasValue || request.RunId.Value <= 0)
                return Json(new Rule61WorkspaceSaveResult { Success = false, Error = "Select a saved run before editing the workspace." });

            var result = await _rule61.BeginWorkspaceEditAsync(request.RunId.Value, user!.Email!, user.FullName);
            if (result.Success)
                await _audit.LogAsync("workspace_edit_started", $"DataAnalyst started editing Rule 61 run {request.RunId.Value}.", user.Id, user.Email);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSql([FromBody] Rule61ValidationRequest request) =>
            Json(new Rule61SqlResult { Success = true, Sql = _rule61.GenerateSql(request) });

        [HttpPost]
        public async Task<IActionResult> SignOffWorkspace([FromBody] Rule61WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0)
                return Json(new { success = false, error = "Select an engagement before signing off." });
            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can sign off." });
            if (!model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Run the validation first so the saved workspace exists." });

            var review = await _rule61.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved validation run could not be found for this engagement." });
            if (!review.IsCurrentRun)
                return Json(new { success = false, error = "History results are read-only. Signoff is only available on the current run." });
            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
                return Json(new { success = false, error = "The assigned data analyst must sign off before this review can be completed." });

            await _rule61.AddOrUpdateSignoffAsync(model.RunId.Value, user!.Email!, model.Comment);
            await _audit.LogAsync("signoff_validation_run", $"Rule 61 signoff saved for run {model.RunId.Value}", user.Id, user.Email);

            var workspace      = await _rule61.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            return Json(new { success = true, message = "Signoff saved.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveWorkspaceSignoff([FromBody] Rule61WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0 || !model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Select a saved run before removing signoff." });
            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can remove signoff." });

            var review = await _rule61.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved validation run could not be found for this engagement." });
            if (!review.IsCurrentRun)
                return Json(new { success = false, error = "History results are read-only." });
            if (!review.CurrentUserHasSignedOff)
                return Json(new { success = false, error = "There is no signoff for your assigned engagement role to remove." });

            await _rule61.RemoveSignoffAsync(model.RunId.Value, user!.Email!);
            await _audit.LogAsync("remove_validation_signoff", $"Signoff removed for Rule 61 run {model.RunId.Value}", user.Id, user.Email);

            var workspace      = await _rule61.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            return Json(new { success = true, message = "Signoff removed.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> DownloadExcel([FromBody] Rule61ValidationSummary summary)
        {
            if (summary?.SavedRunId is int runId)
            {
                var stored = await _rule61.GetStoredSummaryAsync(runId);
                if (stored != null)
                    summary = stored;
            }

            var bytes = BuildExcelExport(summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Rule61_Research_Time_{Ts()}.xlsx");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadCsv([FromBody] Rule61ValidationSummary summary)
        {
            if (summary?.SavedRunId is int runId)
            {
                var stored = await _rule61.GetStoredSummaryAsync(runId);
                if (stored != null)
                    summary = stored;
            }

            var bytes = BuildCsvExport(summary);
            return File(bytes, "text/csv", $"Rule61_Research_Time_{Ts()}.csv");
        }

        [HttpPost]
        public IActionResult DownloadSql([FromBody] Rule61ValidationRequest request)
        {
            var sql = _rule61.GenerateSql(request);
            var bytes = System.Text.Encoding.UTF8.GetBytes(sql);
            return File(bytes, "application/sql", $"Rule61_Research_Time_{Ts()}.sql");
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static string Ts() => DateTime.Now.ToString("yyyyMMdd_HHmmss");

        private static byte[] BuildExcelExport(Rule61ValidationSummary summary)
        {
            using var workbook = new XLWorkbook();

            var summarySheet = workbook.Worksheets.Add("Summary");
            summarySheet.Cell(1, 1).Value = "HEMIS RULE 61 - Masters/Doctoral Research Time Validation";
            summarySheet.Range(1, 1, 1, 2).Merge();
            summarySheet.Cell(1, 1).Style.Font.Bold = true;
            summarySheet.Cell(1, 1).Style.Font.FontSize = 14;

            var summaryRows = new (string Label, string Value)[]
            {
                ("Database", summary.Database),
                ("Timestamp", summary.Timestamp),
                ("STUD Table", summary.StudTable),
                ("QUAL Table", summary.QualTable),
                ("PQM Table", summary.PqmTable),
                ("PG Types", summary.PgTypesText),
                ("STUD Status Column", summary.ColumnMapping.StudStatusCol),
                ("STUD Status Filter", summary.StudStatusValue),
                ("Total Count", summary.TotalCount.ToString()),
                ("Pass Count", summary.PassCount.ToString()),
                ("Fail Count", summary.FailCount.ToString()),
                ("Missing PQM Count", summary.MissingPqmCount.ToString()),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };

            summarySheet.Cell(3, 1).Value = "Field";
            summarySheet.Cell(3, 2).Value = "Value";
            summarySheet.Range(3, 1, 3, 2).Style.Font.Bold = true;
            summarySheet.Range(3, 1, 3, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");

            var summaryRowIndex = 4;
            foreach (var item in summaryRows)
            {
                summarySheet.Cell(summaryRowIndex, 1).Value = item.Label;
                summarySheet.Cell(summaryRowIndex, 2).Value = item.Value;
                summaryRowIndex++;
            }
            summarySheet.Columns(1, 2).AdjustToContents();

            WriteWorksheet(workbook, "Pass Rows", summary.PassRows);
            WriteWorksheet(workbook, "Fail Rows", summary.FailRows);

            using var ms = new System.IO.MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }

        private static byte[] BuildCsvExport(Rule61ValidationSummary summary)
        {
            using var ms = new System.IO.MemoryStream();
            using var sw = new System.IO.StreamWriter(ms, System.Text.Encoding.UTF8);
            sw.WriteLine("\"HEMIS RULE 61 - Research Time Validation\"");
            sw.WriteLine($"\"Database\",\"{summary.Database}\"");
            sw.WriteLine($"\"Timestamp\",\"{summary.Timestamp}\"");
            sw.WriteLine($"\"Status\",\"{summary.Status}\"");
            sw.WriteLine($"\"STUD Status Column\",\"{summary.ColumnMapping.StudStatusCol}\"");
            sw.WriteLine($"\"STUD Status Filter\",\"{summary.StudStatusValue}\"");
            sw.WriteLine($"\"Total\",{summary.TotalCount},\"Pass\",{summary.PassCount},\"Fail\",{summary.FailCount}");
            sw.WriteLine();
            WriteReviewCsv(sw, summary, false);
            sw.Flush();
            return ms.ToArray();
        }

        private static void WriteReviewCsv(System.IO.StreamWriter sw, Rule61ValidationSummary summary, bool exceptionsOnly)
        {
            sw.WriteLine("\"STUD._007\",\"STUD._001\",\"STUD._008\",\"STUD._073\",\"STUD._010\",\"QUAL._001\",\"QUAL._005\",\"QUAL._003\",\"PQM.Authorised_Qualification_Name\",\"PQM.Research_1\",\"Result\",\"Explanation\"");
            var rows = exceptionsOnly ? summary.FailRows : summary.PassRows.Concat(summary.FailRows);
            foreach (var r in rows)
            {
                sw.WriteLine(string.Join(",", new[]
                {
                    CsvValue(r.StudentNo),
                    CsvValue(r.QualCode),
                    CsvValue(r.StudentId),
                    CsvValue(r.StudResearchTime),
                    CsvValue(r.StudStatus),
                    CsvValue(r.QualJoinCode),
                    CsvValue(r.QualType),
                    CsvValue(r.QualName),
                    CsvValue(r.PqmName),
                    CsvValue(r.PqmResearchTime),
                    CsvValue(r.ValidationResult),
                    CsvValue(r.ValidationExplanation)
                }));
            }
        }

        private static string CsvValue(string? value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private static void WriteWorksheet(XLWorkbook workbook, string sheetName, IEnumerable<Rule61ReviewRow> rows)
        {
            var worksheet = workbook.Worksheets.Add(sheetName);
            var headers = new[]
            {
                "STUD._007",
                "STUD._001",
                "STUD._008",
                "STUD._073",
                "STUD._010",
                "QUAL._001",
                "QUAL._005",
                "QUAL._003",
                "PQM.Authorised_Qualification_Name",
                "PQM.Research_1",
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
                worksheet.Cell(rowIndex, 1).Value = row.StudentNo;
                worksheet.Cell(rowIndex, 2).Value = row.QualCode;
                worksheet.Cell(rowIndex, 3).Value = row.StudentId;
                worksheet.Cell(rowIndex, 4).Value = row.StudResearchTime;
                worksheet.Cell(rowIndex, 5).Value = row.StudStatus;
                worksheet.Cell(rowIndex, 6).Value = row.QualJoinCode;
                worksheet.Cell(rowIndex, 7).Value = row.QualType;
                worksheet.Cell(rowIndex, 8).Value = row.QualName;
                worksheet.Cell(rowIndex, 9).Value = row.PqmName;
                worksheet.Cell(rowIndex, 10).Value = row.PqmResearchTime;
                worksheet.Cell(rowIndex, 11).Value = row.ValidationResult;
                worksheet.Cell(rowIndex, 12).Value = row.ValidationExplanation;
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

        private static bool CanViewResults(string role, Rule61WorkspaceStateViewModel? workspace)
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
                return new { success = false, error = "Only the assigned data analyst can configure Rule 61." };
            return await action();
        }
    }
}
