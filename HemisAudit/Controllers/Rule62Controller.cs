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
    public class Rule62Controller : Controller
    {
        private readonly IRule62Service _rule62;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;
        private readonly ILogger<Rule62Controller> _logger;

        public Rule62Controller(
            IRule62Service rule62,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb,
            ILogger<Rule62Controller> logger)
        {
            _rule62 = rule62;
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
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForWorkspace(62, clientId);
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

            var workspace = await _rule62.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
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

            var review = await _rule62.GetSavedRunAsync(id, user?.Email);
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
                62, review.ClientId, clientDetail?.ValidationRuns, role, review.CurrentUserEngagementRole);
            ViewBag.CanOpenWorkspace =
                !isArchived &&
                await _systemDb.CanAccessClientModuleAsync(review.ClientId, user, role) &&
                (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(review.CurrentUserEngagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            review.GeneratedSql = _rule62.GenerateSql(new Rule62ValidationRequest
            {
                ClientId = review.ClientId,
                Database = review.Summary.Database,
                StudTable = review.Summary.StudTable,
                QualTable = review.Summary.QualTable,
                QualTypeCodesText = review.Summary.QualTypeCodesText,
                ColumnMapping = review.Summary.ColumnMapping
            });

            return View(review);
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule62.GetDatabasesAsync(model.Server, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule62.GetTablesAsync(model.Server, model.Database, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetColumns([FromBody] Rule62GetColumnsRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule62.GetColumnsAsync(request.Server, request.Database, request.Driver, request.TableName)));

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] Rule62VerifyRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule62.VerifyTablesAsync(request)));

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] Rule62ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new Rule62ValidationSummary { Success = false, Error = "Select an approved engagement before running validation." });

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule62ValidationSummary { Success = false, Error = "You cannot access this engagement." });

            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new Rule62ValidationSummary { Success = false, Error = "Only the assigned data analyst can run Rule 62." });
            }

            var result = await _rule62.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);
            _logger.LogInformation("Rule62 completed for {Email}. Status={Status}, Total={Total}, Pass={Pass}, Fail={Fail}",
                user?.Email, result.Status, result.TotalCount, result.PassCount, result.FailCount);

            if (result.Success)
            {
                await _audit.LogAsync(
                    "run_validation",
                    $"Rule 62 on client {request.ClientId}: {result.Status} ({result.ExceptionDetailCount} exception rows), run {result.SavedRunId}",
                    user?.Id,
                    user?.Email);
            }

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] Rule62ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!await CanEditAsync(request.ClientId, user, role))
                return Json(new Rule62WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can save a workspace." });

            var result = await _rule62.SaveWorkspaceAsync(request, user!.Email!, user.FullName);
            if (result.Success)
                await _audit.LogAsync("save_validation_workspace", $"DataAnalyst saved Rule 62 workspace for client {request.ClientId}.", user.Id, user.Email);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> BeginWorkspaceEdit([FromBody] Rule62ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!await CanEditAsync(request.ClientId, user, role))
                return Json(new Rule62WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can edit a saved workspace." });
            if (!request.RunId.HasValue || request.RunId.Value <= 0)
                return Json(new Rule62WorkspaceSaveResult { Success = false, Error = "Select a saved run before editing the workspace." });

            var result = await _rule62.BeginWorkspaceEditAsync(request.RunId.Value, user!.Email!, user.FullName);
            if (result.Success)
                await _audit.LogAsync("workspace_edit_started", $"DataAnalyst started editing Rule 62 run {request.RunId.Value}.", user.Id, user.Email);
            return Json(result);
        }

        [HttpPost]
        public IActionResult GenerateSql([FromBody] Rule62ValidationRequest request) =>
            Json(new Rule62SqlResult { Success = true, Sql = _rule62.GenerateSql(request) });
        [HttpPost]
        public IActionResult GenerateRScript([FromBody] Rule62ValidationRequest request) =>
            Json(new Rule62SqlResult { Success = true, Sql = Rule62RScriptGenerator.Generate(request) + RScriptScaffold.BuildAutoExportFooter("Rule62") });

        [HttpPost]
        public async Task<IActionResult> SignOffWorkspace([FromBody] Rule62WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0)
                return Json(new { success = false, error = "Select an engagement before signing off." });
            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can sign off." });
            if (!model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Run the validation first so the saved workspace exists." });

            var review = await _rule62.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved validation run could not be found for this engagement." });
            if (!review.IsCurrentRun)
                return Json(new { success = false, error = "History results are read-only. Signoff is only available on the current run." });
            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
                return Json(new { success = false, error = "The assigned data analyst must sign off before this review can be completed." });

            await _rule62.AddOrUpdateSignoffAsync(model.RunId.Value, user!.Email!, model.Comment);
            await _audit.LogAsync("signoff_validation_run", $"Rule 62 signoff saved for run {model.RunId.Value}", user.Id, user.Email);

            var workspace = await _rule62.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null)
                workspace.ResultsVisible = resultsVisible;
            return Json(new { success = true, message = "Signoff saved.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveWorkspaceSignoff([FromBody] Rule62WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0 || !model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Select a saved run before removing signoff." });
            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can remove signoff." });

            var review = await _rule62.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved validation run could not be found for this engagement." });
            if (!review.IsCurrentRun)
                return Json(new { success = false, error = "History results are read-only." });
            if (!review.CurrentUserHasSignedOff)
                return Json(new { success = false, error = "There is no signoff for your assigned engagement role to remove." });

            await _rule62.RemoveSignoffAsync(model.RunId.Value, user!.Email!);
            await _audit.LogAsync("remove_validation_signoff", $"Signoff removed for Rule 62 run {model.RunId.Value}", user.Id, user.Email);

            var workspace = await _rule62.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null)
                workspace.ResultsVisible = resultsVisible;
            return Json(new { success = true, message = "Signoff removed.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> DownloadExcel([FromBody] Rule62ValidationSummary summary)
        {
            if (summary?.SavedRunId is int runId)
            {
                var stored = await _rule62.GetStoredSummaryAsync(runId);
                if (stored != null)
                    summary = stored;
            }

            summary ??= new Rule62ValidationSummary();
            var bytes = BuildExcelExport(summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Rule62_SQLVALPAC_Results_{Ts()}.xlsx");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadCsv([FromBody] Rule62ValidationSummary summary)
        {
            if (summary?.SavedRunId is int runId)
            {
                var stored = await _rule62.GetStoredSummaryAsync(runId);
                if (stored != null)
                    summary = stored;
            }

            summary ??= new Rule62ValidationSummary();
            var bytes = BuildCsvExport(summary);
            return File(bytes, "text/csv", $"Rule62_SQLVALPAC_Results_{Ts()}.csv");
        }

        [HttpPost]
        public IActionResult DownloadSql([FromBody] Rule62ValidationRequest request)
        {
            var sql = _rule62.GenerateSql(request);
            var bytes = System.Text.Encoding.UTF8.GetBytes(sql);
            return File(bytes, "application/sql", $"Rule62_SQLVALPAC_Results_{Ts()}.sql");
        }

        private static string Ts() => DateTime.Now.ToString("yyyyMMdd_HHmmss");

        private static byte[] BuildExcelExport(Rule62ValidationSummary summary)
        {
            using var workbook = new XLWorkbook();

            var summarySheet = workbook.Worksheets.Add("Summary");
            summarySheet.Cell(1, 1).Value = "HEMIS RULE 62 - SQLVALPAC Approved Exception Review (Full Results)";
            summarySheet.Range(1, 1, 1, 2).Merge();
            summarySheet.Cell(1, 1).Style.Font.Bold = true;
            summarySheet.Cell(1, 1).Style.Font.FontSize = 14;

            var summaryRows = new (string Label, string Value)[]
            {
                ("Database", summary.Database),
                ("Timestamp", summary.Timestamp),
                ("STUD Table", summary.StudTable),
                ("QUAL Table", summary.QualTable),
                ("Configured QUAL._005 Codes", summary.QualTypeCodesText),
                ("Total STUD Rows", summary.TotalCount.ToString()),
                ("Clear Students", summary.PassCount.ToString()),
                ("Students With Exceptions", summary.FailCount.ToString()),
                ("Exception Detail Rows", summary.ExceptionDetailCount.ToString()),
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

            WriteWorksheet(workbook, "Exception Rows", summary.FailRows);
            WriteWorksheet(workbook, "Clear Rows", summary.PassRows);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static byte[] BuildCsvExport(Rule62ValidationSummary summary)
        {
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            writer.WriteLine("\"HEMIS RULE 62 - SQLVALPAC Approved Exception Review\"");
            writer.WriteLine($"\"Database\",\"{summary.Database}\"");
            writer.WriteLine($"\"Timestamp\",\"{summary.Timestamp}\"");
            writer.WriteLine($"\"Status\",\"{summary.Status}\"");
            writer.WriteLine($"\"Configured QUAL._005 Codes\",\"{summary.QualTypeCodesText}\"");
            writer.WriteLine($"\"Total STUD Rows\",{summary.TotalCount},\"Clear Students\",{summary.PassCount},\"Students With Exceptions\",{summary.FailCount},\"Exception Detail Rows\",{summary.ExceptionDetailCount}");
            writer.WriteLine();
            WriteReviewCsv(writer, summary, false);
            writer.Flush();
            return stream.ToArray();
        }

        private static void WriteReviewCsv(StreamWriter writer, Rule62ValidationSummary summary, bool exceptionsOnly)
        {
            writer.WriteLine("\"STUD._007\",\"STUD._001\",\"STUD._008\",\"STUD._072\",\"STUD._015\",\"STUD._010\",\"STUD._022\",\"STUD._023\",\"QUAL._001\",\"QUAL._005\",\"Error Code\",\"Result\",\"Explanation\"");
            var rows = exceptionsOnly ? summary.FailRows : summary.FailRows.Concat(summary.PassRows);
            foreach (var row in rows)
            {
                writer.WriteLine(string.Join(",", new[]
                {
                    CsvValue(row.StudentNo),
                    CsvValue(row.QualCode),
                    CsvValue(row.StudentId),
                    CsvValue(row.PostalPostcode),
                    CsvValue(row.HomePostcode),
                    CsvValue(row.EntranceCategory),
                    CsvValue(row.SecondaryCompletion),
                    CsvValue(row.MatricAggregate),
                    CsvValue(row.QualJoinCode),
                    CsvValue(row.QualType),
                    CsvValue(row.ErrorCode),
                    CsvValue(row.ValidationResult),
                    CsvValue(row.ValidationExplanation)
                }));
            }
        }

        private static string CsvValue(string? value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private static void WriteWorksheet(XLWorkbook workbook, string sheetName, IEnumerable<Rule62ReviewRow> rows)
        {
            var worksheet = workbook.Worksheets.Add(sheetName);
            var headers = new[]
            {
                "STUD._007",
                "STUD._001",
                "STUD._008",
                "STUD._072",
                "STUD._015",
                "STUD._010",
                "STUD._022",
                "STUD._023",
                "QUAL._001",
                "QUAL._005",
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
                worksheet.Cell(rowIndex, 1).Value = row.StudentNo;
                worksheet.Cell(rowIndex, 2).Value = row.QualCode;
                worksheet.Cell(rowIndex, 3).Value = row.StudentId;
                worksheet.Cell(rowIndex, 4).Value = row.PostalPostcode;
                worksheet.Cell(rowIndex, 5).Value = row.HomePostcode;
                worksheet.Cell(rowIndex, 6).Value = row.EntranceCategory;
                worksheet.Cell(rowIndex, 7).Value = row.SecondaryCompletion;
                worksheet.Cell(rowIndex, 8).Value = row.MatricAggregate;
                worksheet.Cell(rowIndex, 9).Value = row.QualJoinCode;
                worksheet.Cell(rowIndex, 10).Value = row.QualType;
                worksheet.Cell(rowIndex, 11).Value = row.ErrorCode;
                worksheet.Cell(rowIndex, 12).Value = row.ValidationResult;
                worksheet.Cell(rowIndex, 13).Value = row.ValidationExplanation;
                rowIndex++;
            }

            worksheet.Columns().AdjustToContents();
        }

        private async Task<string> GetCurrentSystemRoleAsync(ApplicationUser? user)
        {
            var systemRole = await _systemDb.GetSystemRoleAsync(user);
            if (!string.IsNullOrWhiteSpace(systemRole))
                return systemRole!;

            var roles = user != null ? await _users.GetRolesAsync(user) : new List<string>();
            return roles.FirstOrDefault() ?? "";
        }

        private static bool CanViewResults(string role, Rule62WorkspaceStateViewModel? workspace)
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
                return new { success = false, error = "Only the assigned data analyst can configure Rule 62." };
            return (object?)await action() ?? new { success = false, error = "No data returned." };
        }
    }
}
