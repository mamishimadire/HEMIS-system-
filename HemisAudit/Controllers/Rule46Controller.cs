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
    public class Rule46Controller : Controller
    {
        private readonly IRule46Service _rule46;
        private readonly IExportService _export;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;

        public Rule46Controller(
            IRule46Service rule46,
            IExportService export,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb)
        {
            _rule46   = rule46;
            _export   = export;
            _audit    = audit;
            _users    = users;
            _systemDb = systemDb;
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

            ViewBag.Clients = clients.Select(c => new Client
            {
                Id = c.Id, Name = c.EngagementName, FiscalYear = c.MaconomyNumber,
                Status = c.Status, CreatedAt = c.CreatedAt, CreatedByUserId = "", IsActive = true
            }).ToList();
            ViewBag.ClientId          = clientId;
            ViewBag.CurrentSystemRole = role;
            ViewBag.ModuleNavigation  = ModuleSequenceNavigationHelper.BuildForWorkspace(46, clientId);
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

            var workspace      = await _rule46.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;

            if (workspace != null && !resultsVisible) workspace.Summary = null;

            return Json(new { success = true, hasWorkspace = workspace != null, resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule46.GetDatabasesAsync(model.Server, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule46.GetTablesAsync(model.Server, model.Database, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetColumns([FromBody] Rule37GetColumnsRequest model) =>
            Json(await RequireDataAnalystAsync(async () =>
                await _rule46.GetColumnsAsync(model.Server, model.Database, model.Driver, model.TableName)));

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] Rule46ValidationRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule46.VerifyTablesAsync(request)));

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] Rule46ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new Rule46ValidationSummary { Success = false, Error = "Select an approved engagement before running validation." });
            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule46ValidationSummary { Success = false, Error = "You cannot access this engagement." });
            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return Json(new Rule46ValidationSummary { Success = false, Error = "Only the assigned data analyst can run Rule 46." });

            async Task<Rule46ValidationSummary> ExecuteAsync(IRule46Service svc, IAuditLogService auditSvc)
            {
                var result = await svc.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);
                if (result.Success)
                    await auditSvc.LogAsync("run_validation",
                        $"Rule 46 on client {request.ClientId}: {result.Status} ({result.FailCount} exceptions), run {result.SavedRunId}",
                        user?.Id, user?.Email);
                return result;
            }

            if (ValidationOperationHttpHelper.IsAsyncRequested(Request))
            {
                return ValidationOperationHttpHelper.Queue(this,
                    HttpContext.RequestServices.GetRequiredService<IValidationOperationService>(),
                    ValidationOperationHttpHelper.ResolveOwnerKey(User), "Rule 46 validation",
                    async (sp, ct) => await ExecuteAsync(sp.GetRequiredService<IRule46Service>(), sp.GetRequiredService<IAuditLogService>()));
            }
            return Json(await ExecuteAsync(_rule46, _audit));
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] Rule46ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!await CanEditWorkspaceAsync(request.ClientId, user, role))
                return Json(new Rule46WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can save a workspace." });
            var result = await _rule46.SaveWorkspaceAsync(request, user!.Email!, user.FullName);
            if (result.Success)
                await _audit.LogAsync("save_validation_workspace", $"DataAnalyst saved Rule 46 workspace for client {request.ClientId}.", user?.Id, user?.Email);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> BeginWorkspaceEdit([FromBody] Rule46ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!await CanEditWorkspaceAsync(request.ClientId, user, role))
                return Json(new Rule46WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can edit a saved workspace." });
            if (!request.RunId.HasValue || request.RunId.Value <= 0)
                return Json(new Rule46WorkspaceSaveResult { Success = false, Error = "Select a saved run before editing." });
            var result = await _rule46.BeginWorkspaceEditAsync(request.RunId.Value, user!.Email!, user.FullName);
            if (result.Success)
                await _audit.LogAsync("workspace_edit_started", $"DataAnalyst started editing Rule 46 run {request.RunId.Value}.", user?.Id, user?.Email);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SignOffWorkspace([FromBody] Rule46WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0)
                return Json(new { success = false, error = "Select an engagement before signing off." });
            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can sign off." });
            if (!model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Run validation first." });

            var review = await _rule46.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved run could not be found for this engagement." });

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
                return Json(new { success = false, error = "Archived engagements are read-only." });
            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
                return Json(new { success = false, error = "The assigned data analyst must sign off before this review can be completed." });

            try
            {
                await _rule46.AddOrUpdateSignoffAsync(model.RunId.Value, user!.Email!, model.Comment);
                await _audit.LogAsync("signoff_validation_run", $"Signed off Rule 46 run {model.RunId.Value}", user.Id, user.Email);
            }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }

            var workspace      = await _rule46.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            return Json(new { success = true, message = "Signoff saved.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveWorkspaceSignoff([FromBody] Rule46WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (model.ClientId <= 0 || !model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Select a saved run before removing signoff." });
            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can remove signoff." });
            var review = await _rule46.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved run could not be found." });
            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true) return Json(new { success = false, error = "Archived engagements are read-only." });
            if (!review.CurrentUserHasSignedOff)  return Json(new { success = false, error = "There is no signoff for your role to remove." });
            try { await _rule46.RemoveSignoffAsync(model.RunId.Value, user!.Email!); }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
            var workspace      = await _rule46.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            await _audit.LogAsync("remove_validation_signoff", $"Removed signoff for Rule 46 run {model.RunId.Value}", user.Id, user.Email);
            return Json(new { success = true, message = "Signoff removed.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSql([FromBody] Rule46ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (request.ClientId > 0 && !await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule46SqlResult { Success = false, Error = "You cannot access this engagement." });
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return Json(new Rule46SqlResult { Success = false, Error = "Only the assigned data analyst can generate SQL." });
            return Json(new Rule46SqlResult
            {
                Success = true,
                Sql     = _rule46.GenerateValidationSql(request)
            });
        }
        [HttpPost]
        public async Task<IActionResult> GenerateRScript([FromBody] Rule46ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId > 0 && !await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule46SqlResult { Success = false, Error = "You cannot access this engagement." });

            return Json(await RequireDataAnalystAsync(async () => new Rule46SqlResult
            {
                Success = true,
                Sql = Rule46RScriptGenerator.Generate(request) + RScriptScaffold.BuildAutoExportFooter("Rule46")
            }));
        }

        [HttpGet]
        public async Task<IActionResult> DownloadCsv([FromQuery] int runId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule46.GetSavedRunAsync(runId, user?.Email);
            if (review == null || !await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role)) return NotFound();
            if (!ValidationRunAccessPolicy.CanDownloadSignedResults(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
            {
                TempData["Error"] = "The assigned data analyst must sign off before downloading.";
                return RedirectToAction(nameof(Index));
            }
            var bytes = BuildCombinedCsvBytes(review.Summary!);
            return File(bytes, "text/csv", $"Rule46_Foundation_Chain_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadExcel([FromQuery] int runId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule46.GetSavedRunAsync(runId, user?.Email);
            if (review == null || !await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role)) return NotFound();
            if (!ValidationRunAccessPolicy.CanDownloadSignedResults(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
            {
                TempData["Error"] = "The assigned data analyst must sign off before downloading.";
                return RedirectToAction(nameof(Index));
            }

            var bytes = BuildExcelExport(review.Summary!);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Rule46_Foundation_Chain_Run_{runId}.xlsx");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSql([FromQuery] int runId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule46.GetSavedRunAsync(runId, user?.Email);
            if (review == null || !await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role)) return NotFound();
            var s = review.Summary!;
            var req = new Rule46ValidationRequest
            {
                Database = s.Database, StudTable = s.StudTable, StudKey = s.StudKey, StudIdCol = s.StudIdCol,
                Stud007Col = s.Stud007Col,
                Stud010Col = s.Stud010Col,
                Stud012Col = s.Stud012Col,
                Stud026Col = s.Stud026Col,
                StudFilterCol = s.StudFilterCol, StudFilterValue = s.StudFilterValue,
                QualTable = s.QualTable, QualKey = s.QualKey, QualNameCol = s.QualNameCol,
                PqmTable = s.PqmTable, PqmNameCol = s.PqmNameCol
            };
            var sqlText = _rule46.GenerateValidationSql(req);
            var bytes = _export.ExportSql(sqlText);
            return File(bytes, "application/sql", $"Rule46_Foundation_Chain_Run_{runId}.sql");
        }

        private static byte[] BuildCsvBytes(Rule46ValidationSummary summary)
        {
            using var ms = new System.IO.MemoryStream();
            using var sw = new System.IO.StreamWriter(ms, System.Text.Encoding.UTF8);
            sw.WriteLine($"\"HEMIS RULE 46 Ã¢â‚¬â€œ Foundation Student PQM Validation\"");
            sw.WriteLine($"\"Database\",\"{summary.Database}\"");
            sw.WriteLine($"\"Timestamp\",\"{summary.Timestamp}\"");
            sw.WriteLine($"\"Total\",{summary.TotalValidated},\"Pass\",{summary.PassCount},\"Fail\",{summary.FailCount},\"Exception Rate\",{summary.ExceptionRate:0.00}%");
            sw.WriteLine();
            sw.WriteLine("\"Row\",\"Stud_ID\",\"Stud_Filter\",\"Qual_ID\",\"Qual_Name\",\"PQM_Name\",\"Result\",\"Detail\"");
            foreach (var row in summary.ValidationRows)
            {
                static string Q(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
                sw.WriteLine($"{row.RowNumber},{Q(row.StudId)},{Q(row.StudFilterValue)},{Q(row.QualId)},{Q(row.QualName)},{Q(row.PqmName)},{Q(row.ValidationResult)},{Q(row.ResultDetail)}");
            }
            sw.Flush();
            return ms.ToArray();
        }

        private static byte[] BuildCombinedCsvBytes(Rule46ValidationSummary summary)
        {
            using var ms = new System.IO.MemoryStream();
            using var sw = new System.IO.StreamWriter(ms, System.Text.Encoding.UTF8);
            sw.WriteLine($"\"HEMIS RULE 46 - Foundation Student Chain Validation\"");
            sw.WriteLine($"\"Database\",\"{summary.Database}\"");
            sw.WriteLine($"\"Timestamp\",\"{summary.Timestamp}\"");
            sw.WriteLine($"\"Total\",{summary.TotalValidated},\"Pass\",{summary.PassCount},\"Fail\",{summary.FailCount},\"Exception Rate\",{summary.ExceptionRate:0.00}%");
            sw.WriteLine();
            sw.WriteLine("\"Row\",\"Stud_Key\",\"Stud__008\",\"Stud__007\",\"Stud__010\",\"Stud__012\",\"Stud__026\",\"STUD__106\",\"Qual_Key\",\"Qual_Name\",\"PQM_Name\",\"Result\",\"Detail\"");
            foreach (var row in summary.ValidationRows)
            {
                static string Q(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
                sw.WriteLine($"{row.RowNumber},{Q(row.StudId)},{Q(row.StudentId)},{Q(row.Stud007)},{Q(row.Stud010)},{Q(row.Stud012)},{Q(row.Stud026)},{Q(row.StudFilterValue)},{Q(row.QualId)},{Q(row.QualName)},{Q(row.PqmName)},{Q(row.ValidationResult)},{Q(row.ResultDetail)}");
            }
            sw.Flush();
            return ms.ToArray();
        }

        private static byte[] BuildExcelExport(Rule46ValidationSummary summary)
        {
            using var workbook = new XLWorkbook();

            var summarySheet = workbook.Worksheets.Add("Summary");
            summarySheet.Cell(1, 1).Value = "HEMIS RULE 46 - Foundation Student Chain Validation";
            summarySheet.Range(1, 1, 1, 2).Merge();
            summarySheet.Cell(1, 1).Style.Font.Bold = true;
            summarySheet.Cell(1, 1).Style.Font.FontSize = 14;

            var summaryRows = new (string Label, string Value)[]
            {
                ("Database", summary.Database),
                ("Timestamp", summary.Timestamp),
                ("STUD Table", summary.StudTable),
                ("STUD Key", summary.StudKey),
                ("STUD ID Column", summary.StudIdCol),
                ("STUD _007 Column", summary.Stud007Col),
                ("STUD _010 Column", summary.Stud010Col),
                ("STUD _012 Column", summary.Stud012Col),
                ("STUD _026 Column", summary.Stud026Col),
                ("Foundation Filter", $"{summary.StudTable}.{summary.StudFilterCol} = '{summary.StudFilterValue}'"),
                ("QUAL Table", summary.QualTable),
                ("PQM Table", summary.PqmTable),
                ("PQM Match Rule", $"{summary.QualTable}.{summary.QualNameCol} = {summary.PqmTable}.{summary.PqmNameCol}"),
                ("Total Count", summary.TotalValidated.ToString()),
                ("Pass Count", summary.PassCount.ToString()),
                ("Fail Count", summary.FailCount.ToString()),
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

            var allRows = summary.ValidationRows ?? new List<Rule46ValidationRow>();
            WriteExcelWorksheet(workbook, "All", allRows);
            WriteExcelWorksheet(workbook, "PASS only",
                allRows.Where(r => string.Equals(r.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase)));
            WriteExcelWorksheet(workbook, "FAIL only",
                allRows.Where(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)));

            using var ms = new System.IO.MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }

        private static void WriteExcelWorksheet(XLWorkbook workbook, string sheetName, IEnumerable<Rule46ValidationRow> rows)
        {
            var worksheet = workbook.Worksheets.Add(sheetName);
            var headers = new[]
            {
                "Row",
                "STUD Key",
                "STUD._008",
                "STUD._007",
                "STUD._010",
                "STUD._012",
                "STUD._026",
                "STUD._106",
                "QUAL Key",
                "QUAL Name",
                "PQM.Authorised_Qualification_Name",
                "Result",
                "Detail"
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
                worksheet.Cell(rowIndex, 1).Value  = row.RowNumber;
                worksheet.Cell(rowIndex, 2).Value  = row.StudId;
                worksheet.Cell(rowIndex, 3).Value  = row.StudentId;
                worksheet.Cell(rowIndex, 4).Value  = row.Stud007;
                worksheet.Cell(rowIndex, 5).Value  = row.Stud010;
                worksheet.Cell(rowIndex, 6).Value  = row.Stud012;
                worksheet.Cell(rowIndex, 7).Value  = row.Stud026;
                worksheet.Cell(rowIndex, 8).Value  = row.StudFilterValue;
                worksheet.Cell(rowIndex, 9).Value  = row.QualId;
                worksheet.Cell(rowIndex, 10).Value = row.QualName;
                worksheet.Cell(rowIndex, 11).Value = row.PqmName;
                worksheet.Cell(rowIndex, 12).Value = row.ValidationResult;
                worksheet.Cell(rowIndex, 13).Value = row.ResultDetail;
                rowIndex++;
            }

            worksheet.Columns().AdjustToContents();
        }

        private static bool CanViewWorkspaceResults(string role, Rule46WorkspaceStateViewModel? workspace)
        {
            if (workspace == null) return false;
            return ValidationRunAccessPolicy.CanViewSignedResults(role, workspace.CurrentUserEngagementRole, workspace.HasDataAnalystSignoff);
        }

        private async Task<string> GetCurrentSystemRoleAsync(ApplicationUser? user)
        {
            var systemRole = await _systemDb.GetSystemRoleAsync(user);
            if (!string.IsNullOrWhiteSpace(systemRole)) return systemRole!;
            var roles = user != null ? await _users.GetRolesAsync(user) : new List<string>();
            return roles.FirstOrDefault() ?? "";
        }

        private async Task<bool> CanEditWorkspaceAsync(int clientId, ApplicationUser? user, string role)
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
                return new { success = false, error = "Only the assigned data analyst can configure or run Rule 46." };
            return await action();
        }
    }
}
