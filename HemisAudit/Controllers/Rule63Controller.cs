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
    public class Rule63Controller : Controller
    {
        private readonly IRule63Service _rule63;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;
        private readonly ILogger<Rule63Controller> _logger;

        public Rule63Controller(
            IRule63Service rule63,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb,
            ILogger<Rule63Controller> logger)
        {
            _rule63 = rule63;
            _audit = audit;
            _users = users;
            _systemDb = systemDb;
            _logger = logger;
        }

        public async Task<IActionResult> Index(int clientId = 0)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();

            if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "Trainee", StringComparison.OrdinalIgnoreCase) &&
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
                    Id = c.Id,
                    Name = c.EngagementName,
                    FiscalYear = c.MaconomyNumber,
                    Status = c.Status,
                    CreatedAt = c.CreatedAt,
                    CreatedByUserId = "",
                    IsActive = true
                })
                .ToList();
            ViewBag.ClientId = clientId;
            ViewBag.CurrentSystemRole = role;
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForWorkspace(63, clientId);
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetWorkspaceState(int clientId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();

            if (clientId <= 0)
                return Json(new { success = true, hasWorkspace = false });

            if (!await _systemDb.CanAccessClientResultsAsync(clientId, user, role))
            {
                Response.StatusCode = StatusCodes.Status403Forbidden;
                return Json(new { success = false, error = "You cannot access this engagement." });
            }

            var workspace = await _rule63.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null)
                workspace.ResultsVisible = resultsVisible;
            if (workspace != null && !resultsVisible)
                workspace.Summary = null;

            return Json(new { success = true, hasWorkspace = workspace != null, resultsVisible, workspace });
        }

        [HttpGet]
        public async Task<IActionResult> Run(int id)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();

            var review = await _rule63.GetSavedRunAsync(id, user?.Email);
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

            var clientDetail = await _systemDb.GetClientDetailAsync(review.ClientId, user, role);
            var isArchived = clientDetail?.IsArchived == true;
            ViewBag.IsArchived = isArchived;
            ViewBag.CanManageEngagement =
                string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForSavedRun(
                63, review.ClientId, clientDetail?.ValidationRuns, role, review.CurrentUserEngagementRole);
            ViewBag.CanOpenWorkspace =
                !isArchived &&
                await _systemDb.CanAccessClientModuleAsync(review.ClientId, user, role) &&
                (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(review.CurrentUserEngagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            review.GeneratedSql = _rule63.GenerateSql(new Rule63ValidationRequest
            {
                ClientId = review.ClientId,
                Database = review.Summary.Database,
                StudTable = review.Summary.StudTable,
                QualTable = review.Summary.QualTable,
                CregTable = review.Summary.CregTable,
                ColumnMapping = review.Summary.ColumnMapping
            });

            return View(review);
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule63.GetDatabasesAsync(model.Server, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule63.GetTablesAsync(model.Server, model.Database, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetColumns([FromBody] Rule63GetColumnsRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule63.GetColumnsAsync(request.Server, request.Database, request.Driver, request.TableName)));

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] Rule63VerifyRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule63.VerifyTablesAsync(request)));

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] Rule63ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new Rule63ValidationSummary { Success = false, Error = "Select an approved engagement before running validation." });

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule63ValidationSummary { Success = false, Error = "You cannot access this engagement." });

            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new Rule63ValidationSummary { Success = false, Error = "Only the assigned data analyst can run Rule 63." });
            }

            var result = await _rule63.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);
            _logger.LogInformation("Rule63 completed for {Email}. Status={Status}, Total={Total}, Pass={Pass}, Fail={Fail}",
                user?.Email, result.Status, result.TotalCount, result.PassCount, result.FailCount);

            if (result.Success)
            {
                await _audit.LogAsync(
                    "run_validation",
                    $"Rule 63 on client {request.ClientId}: {result.Status} ({result.FailCount} exception rows), run {result.SavedRunId}",
                    user?.Id,
                    user?.Email);
            }

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] Rule63ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!await CanEditAsync(request.ClientId, user, role))
                return Json(new Rule63WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can save a workspace." });

            var result = await _rule63.SaveWorkspaceAsync(request, user!.Email!, user.FullName);
            if (result.Success)
                await _audit.LogAsync("save_validation_workspace", $"DataAnalyst saved Rule 63 workspace for client {request.ClientId}.", user.Id, user.Email);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> BeginWorkspaceEdit([FromBody] Rule63ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!await CanEditAsync(request.ClientId, user, role))
                return Json(new Rule63WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can edit a saved workspace." });
            if (!request.RunId.HasValue || request.RunId.Value <= 0)
                return Json(new Rule63WorkspaceSaveResult { Success = false, Error = "Select a saved run before editing the workspace." });

            var result = await _rule63.BeginWorkspaceEditAsync(request.RunId.Value, user!.Email!, user.FullName);
            if (result.Success)
                await _audit.LogAsync("workspace_edit_started", $"DataAnalyst started editing Rule 63 run {request.RunId.Value}.", user.Id, user.Email);
            return Json(result);
        }

        [HttpPost]
        public IActionResult GenerateSql([FromBody] Rule63ValidationRequest request) =>
            Json(new Rule63SqlResult { Success = true, Sql = _rule63.GenerateSql(request) });
        [HttpPost]
        public IActionResult GenerateRScript([FromBody] Rule63ValidationRequest request) =>
            Json(new Rule63SqlResult { Success = true, Sql = Rule63RScriptGenerator.Generate(request) + RScriptScaffold.BuildAutoExportFooter("Rule63") });

        [HttpPost]
        public async Task<IActionResult> SignOffWorkspace([FromBody] Rule63WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0)
                return Json(new { success = false, error = "Select an engagement before signing off." });
            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can sign off." });
            if (!model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Run the validation first so the saved workspace exists." });

            var review = await _rule63.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved validation run could not be found for this engagement." });
            if (!review.IsCurrentRun)
                return Json(new { success = false, error = "History results are read-only. Signoff is only available on the current run." });
            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
                return Json(new { success = false, error = "The assigned data analyst must sign off before this review can be completed." });

            await _rule63.AddOrUpdateSignoffAsync(model.RunId.Value, user!.Email!, model.Comment);
            await _audit.LogAsync("signoff_validation_run", $"Rule 63 signoff saved for run {model.RunId.Value}", user.Id, user.Email);

            var workspace = await _rule63.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null)
                workspace.ResultsVisible = resultsVisible;
            return Json(new { success = true, message = "Signoff saved.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveWorkspaceSignoff([FromBody] Rule63WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0 || !model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Select a saved run before removing signoff." });
            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can remove signoff." });

            var review = await _rule63.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved validation run could not be found for this engagement." });
            if (!review.IsCurrentRun)
                return Json(new { success = false, error = "History results are read-only." });
            if (!review.CurrentUserHasSignedOff)
                return Json(new { success = false, error = "There is no signoff for your assigned engagement role to remove." });

            await _rule63.RemoveSignoffAsync(model.RunId.Value, user!.Email!);
            await _audit.LogAsync("remove_validation_signoff", $"Signoff removed for Rule 63 run {model.RunId.Value}", user.Id, user.Email);

            var workspace = await _rule63.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null)
                workspace.ResultsVisible = resultsVisible;
            return Json(new { success = true, message = "Signoff removed.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> DownloadExcel([FromBody] Rule63ValidationSummary summary)
        {
            var resolved = await ResolveDownloadSummaryAsync(summary);
            var bytes = BuildExcelExport(resolved);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Rule63_Qualification_Code_Reference_{Ts()}.xlsx");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadCsv([FromBody] Rule63ValidationSummary summary)
        {
            var resolved = await ResolveDownloadSummaryAsync(summary);
            var bytes = BuildCsvExport(resolved);
            return File(bytes, "text/csv", $"Rule63_Qualification_Code_Reference_{Ts()}.csv");
        }

        [HttpPost]
        public IActionResult DownloadSql([FromBody] Rule63ValidationRequest request)
        {
            var sql = _rule63.GenerateSql(request);
            var bytes = System.Text.Encoding.UTF8.GetBytes(sql);
            return File(bytes, "application/sql", $"Rule63_Qualification_Code_Reference_{Ts()}.sql");
        }

        private static string Ts() => DateTime.Now.ToString("yyyyMMdd_HHmmss");

        private static byte[] BuildExcelExport(Rule63ValidationSummary summary)
        {
            var passRows = summary.PassRows ?? new List<Rule63ReviewRow>();
            var failRows = summary.FailRows ?? new List<Rule63ReviewRow>();
            using var workbook = new XLWorkbook();

            var summarySheet = workbook.Worksheets.Add("Summary");
            summarySheet.Cell(1, 1).Value = "HEMIS RULE 63 - Qualification Code Reference Validation";
            summarySheet.Range(1, 1, 1, 2).Merge();
            summarySheet.Cell(1, 1).Style.Font.Bold = true;
            summarySheet.Cell(1, 1).Style.Font.FontSize = 14;

            var summaryRows = new (string Label, string Value)[]
            {
                ("Database", summary.Database),
                ("Timestamp", summary.Timestamp),
                ("STUD Table", summary.StudTable),
                ("QUAL Table", summary.QualTable),
                ("CREG Table", summary.CregTable),
                ("Total Rows", summary.TotalCount.ToString()),
                ("Clear Rows", summary.PassCount.ToString()),
                ("Flagged Rows", summary.FailCount.ToString()),
                ("Exception Rate", $"{summary.ExceptionRate:F2}%"),
                ("Status", summary.Status)
            };

            summarySheet.Cell(3, 1).Value = "Field";
            summarySheet.Cell(3, 2).Value = "Value";
            summarySheet.Range(3, 1, 3, 2).Style.Font.Bold = true;
            summarySheet.Range(3, 1, 3, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");

            var rowIndex = 4;
            foreach (var item in summaryRows)
            {
                summarySheet.Cell(rowIndex, 1).Value = item.Label;
                summarySheet.Cell(rowIndex, 2).Value = item.Value;
                rowIndex++;
            }
            summarySheet.Columns(1, 2).AdjustToContents();

            WriteWorksheet(workbook, "Flagged Rows", failRows);
            WriteWorksheet(workbook, "Clear Rows", passRows);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static byte[] BuildCsvExport(Rule63ValidationSummary summary)
        {
            var passRows = summary.PassRows ?? new List<Rule63ReviewRow>();
            var failRows = summary.FailRows ?? new List<Rule63ReviewRow>();
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            writer.WriteLine("\"HEMIS RULE 63 - Qualification Code Reference Validation\"");
            writer.WriteLine($"\"Database\",\"{summary.Database}\"");
            writer.WriteLine($"\"Timestamp\",\"{summary.Timestamp}\"");
            writer.WriteLine($"\"Status\",\"{summary.Status}\"");
            writer.WriteLine($"\"Total Rows\",{summary.TotalCount},\"Clear Rows\",{summary.PassCount},\"Flagged Rows\",{summary.FailCount}");
            writer.WriteLine();
            WriteReviewCsv(writer, passRows, failRows, false);
            writer.Flush();
            return stream.ToArray();
        }

        private static void WriteReviewCsv(StreamWriter writer, IReadOnlyCollection<Rule63ReviewRow> passRows, IReadOnlyCollection<Rule63ReviewRow> failRows, bool exceptionsOnly)
        {
            writer.WriteLine("\"Source Table\",\"Student No\",\"Qualification Code\",\"QUAL._001\",\"Error Code\",\"Result\",\"Explanation\"");
            var rows = exceptionsOnly ? failRows : failRows.Concat(passRows);
            foreach (var row in rows)
            {
                writer.WriteLine(string.Join(",", new[]
                {
                    CsvValue(row.SourceTable),
                    CsvValue(row.StudentNo),
                    CsvValue(row.QualCode),
                    CsvValue(row.QualJoinCode),
                    CsvValue(row.ErrorCode),
                    CsvValue(row.ValidationResult),
                    CsvValue(row.ValidationExplanation)
                }));
            }
        }

        private static string CsvValue(string? value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private static void WriteWorksheet(XLWorkbook workbook, string sheetName, IEnumerable<Rule63ReviewRow> rows)
        {
            var worksheet = workbook.Worksheets.Add(sheetName);
            var headers = new[]
            {
                "Source Table",
                "Student No",
                "Qualification Code",
                "QUAL._001",
                "Error Code",
                "Result",
                "Explanation"
            };

            for (var index = 0; index < headers.Length; index++)
            {
                var cell = worksheet.Cell(1, index + 1);
                cell.Value = headers[index];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");
            }

            var rowIndex = 2;
            foreach (var row in rows)
            {
                worksheet.Cell(rowIndex, 1).Value = row.SourceTable;
                worksheet.Cell(rowIndex, 2).Value = row.StudentNo;
                worksheet.Cell(rowIndex, 3).Value = row.QualCode;
                worksheet.Cell(rowIndex, 4).Value = row.QualJoinCode;
                worksheet.Cell(rowIndex, 5).Value = row.ErrorCode;
                worksheet.Cell(rowIndex, 6).Value = row.ValidationResult;
                worksheet.Cell(rowIndex, 7).Value = row.ValidationExplanation;
                rowIndex++;
            }

            worksheet.Columns().AdjustToContents();
        }

        private async Task<Rule63ValidationSummary> ResolveDownloadSummaryAsync(Rule63ValidationSummary? summary)
        {
            if (summary?.SavedRunId is int runId && runId > 0)
            {
                var stored = await _rule63.GetStoredSummaryAsync(runId);
                if (stored != null)
                    summary = stored;
            }

            summary ??= new Rule63ValidationSummary();
            summary.PassRows ??= new List<Rule63ReviewRow>();
            summary.FailRows ??= new List<Rule63ReviewRow>();

            if (summary.PassCount <= 0 && summary.PassRows.Count > 0)
                summary.PassCount = summary.PassRows.Count;
            if (summary.FailCount <= 0 && summary.FailRows.Count > 0)
                summary.FailCount = summary.FailRows.Count;
            if (summary.TotalCount <= 0)
                summary.TotalCount = summary.PassCount + summary.FailCount;

            summary.ExceptionDetailCount = Math.Max(summary.ExceptionDetailCount, summary.FailRows.Count);
            summary.ExceptionRate = summary.TotalCount == 0
                ? 0m
                : Math.Round((decimal)summary.FailCount / summary.TotalCount * 100m, 2);

            if (string.IsNullOrWhiteSpace(summary.Status))
                summary.Status = summary.FailCount == 0 ? "PASS" : "FAIL";

            return summary;
        }

        private async Task<string> GetCurrentSystemRoleAsync(ApplicationUser? user)
        {
            var systemRole = await _systemDb.GetSystemRoleAsync(user);
            if (!string.IsNullOrWhiteSpace(systemRole))
                return systemRole!;

            var roles = user != null ? await _users.GetRolesAsync(user) : new List<string>();
            return roles.FirstOrDefault() ?? "";
        }

        private static bool CanViewResults(string role, Rule63WorkspaceStateViewModel? workspace)
        {
            if (workspace == null)
                return false;

            return ValidationRunAccessPolicy.CanViewSignedResults(role, workspace.CurrentUserEngagementRole, workspace.HasDataAnalystSignoff);
        }

        private async Task<bool> CanEditAsync(int clientId, ApplicationUser? user, string role)
        {
            if (user == null || !string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) || clientId <= 0)
                return false;
            if (!await _systemDb.CanAccessClientResultsAsync(clientId, user, role))
                return false;

            var engagementRole = await _systemDb.GetEngagementRoleAsync(clientId, user, role);
            return ValidationRunAccessPolicy.IsAssignedDataAnalyst(engagementRole);
        }

        private async Task<object> RequireDataAnalystAsync<T>(Func<Task<T>> action)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return new { success = false, error = "Only the assigned data analyst can configure Rule 63." };
            return (object?)await action() ?? new { success = false, error = "No data returned." };
        }
    }
}
