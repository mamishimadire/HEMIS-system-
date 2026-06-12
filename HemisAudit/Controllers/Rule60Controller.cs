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
    public class Rule60Controller : Controller
    {
        private readonly IRule60Service _rule60;
        private readonly IExportService _export;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;

        public Rule60Controller(
            IRule60Service rule60,
            IExportService export,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb)
        {
            _rule60   = rule60;
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

            ViewBag.Clients = clients
                .Select(c => new Client
                {
                    Id              = c.Id,
                    Name            = c.EngagementName,
                    FiscalYear      = c.MaconomyNumber,
                    Status          = c.Status,
                    CreatedAt       = c.CreatedAt,
                    CreatedByUserId = "",
                    IsActive        = true
                })
                .ToList();
            ViewBag.ClientId          = clientId;
            ViewBag.CurrentSystemRole = role;
            ViewBag.ModuleNavigation  = ModuleSequenceNavigationHelper.BuildForWorkspace(60, clientId);
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

            var workspace      = await _rule60.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;

            if (workspace != null && !resultsVisible) workspace.Summary = null;

            return Json(new { success = true, hasWorkspace = workspace != null, resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule60.GetDatabasesAsync(model.Server, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule60.GetTablesAsync(model.Server, model.Database, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetColumns([FromBody] Rule41GetColumnsRequest model) =>
            Json(await RequireDataAnalystAsync(async () =>
                await _rule60.GetColumnsAsync(model.Server, model.Database, model.Driver, model.TableName)));

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] Rule41VerifyRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule60.VerifyTablesAsync(request)));

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] Rule41ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new Rule41ValidationSummary { Success = false, Error = "Select an approved engagement before running validation." });

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule41ValidationSummary { Success = false, Error = "You cannot access this engagement." });

            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return Json(new Rule41ValidationSummary { Success = false, Error = "Only the assigned data analyst can run Rule 60." });

            async Task<Rule41ValidationSummary> ExecuteAsync(IRule60Service svc, IAuditLogService auditSvc)
            {
                var result = await svc.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);
                if (result.Success)
                {
                    await auditSvc.LogAsync(
                        "run_validation",
                        $"Rule 60 on client {request.ClientId}: {result.Status} ({result.DisagreeCount + result.MissingCount} exceptions), run {result.SavedRunId}",
                        user?.Id, user?.Email);
                }
                return result;
            }

            if (ValidationOperationHttpHelper.IsAsyncRequested(Request))
            {
                return ValidationOperationHttpHelper.Queue(
                    this,
                    HttpContext.RequestServices.GetRequiredService<IValidationOperationService>(),
                    ValidationOperationHttpHelper.ResolveOwnerKey(User),
                    "Rule 60 validation",
                    async (sp, ct) => await ExecuteAsync(
                        sp.GetRequiredService<IRule60Service>(),
                        sp.GetRequiredService<IAuditLogService>()));
            }

            return Json(await ExecuteAsync(_rule60, _audit));
        }

        [HttpPost]
        public async Task<IActionResult> BeginWorkspaceEdit([FromBody] Rule41ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (!await CanEditWorkspaceAsync(request.ClientId, user, role))
                return Json(new Rule41WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can edit a saved workspace." });

            if (!request.RunId.HasValue || request.RunId.Value <= 0)
                return Json(new Rule41WorkspaceSaveResult { Success = false, Error = "Select a saved run before editing the workspace." });

            var result = await _rule60.BeginWorkspaceEditAsync(request.RunId.Value, user!.Email!, user.FullName);
            if (result.Success)
                await _audit.LogAsync("workspace_edit_started", $"DataAnalyst started editing Rule 60 run {request.RunId.Value}.", user?.Id, user?.Email);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] Rule41ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (!await CanEditWorkspaceAsync(request.ClientId, user, role))
                return Json(new Rule41WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can save a workspace." });

            var result = await _rule60.SaveWorkspaceAsync(request, user!.Email!, user.FullName);
            if (result.Success)
                await _audit.LogAsync("save_validation_workspace", $"DataAnalyst saved Rule 60 workspace for client {request.ClientId}.", user?.Id, user?.Email);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SignOffWorkspace([FromBody] Rule41WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0)
                return Json(new { success = false, error = "Select an engagement before signing off." });

            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can sign off." });

            if (!model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Run validation first." });

            var review = await _rule60.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved run could not be found for this engagement." });

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
                return Json(new { success = false, error = "Archived engagements are read-only." });

            if (!review.IsCurrentRun)
                return Json(new { success = false, error = "History results are read-only." });

            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
                return Json(new { success = false, error = "The assigned data analyst must sign off before this review can be completed." });

            try
            {
                await _rule60.AddOrUpdateSignoffAsync(model.RunId.Value, user!.Email!, model.Comment);
                await _audit.LogAsync("signoff_validation_run", $"Signed off Rule 60 run {model.RunId.Value}", user.Id, user.Email);
            }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }

            var workspace      = await _rule60.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            return Json(new { success = true, message = "Signoff saved.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveWorkspaceSignoff([FromBody] Rule41WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0 || !model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Select a saved run before removing signoff." });

            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can remove signoff." });

            var review = await _rule60.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved run could not be found." });

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
                return Json(new { success = false, error = "Archived engagements are read-only." });

            if (!review.IsCurrentRun)
                return Json(new { success = false, error = "History results are read-only." });

            if (!review.CurrentUserHasSignedOff)
                return Json(new { success = false, error = "There is no signoff for your role to remove." });

            try { await _rule60.RemoveSignoffAsync(model.RunId.Value, user!.Email!); }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }

            var workspace      = await _rule60.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            await _audit.LogAsync("remove_validation_signoff", $"Removed signoff for Rule 60 run {model.RunId.Value}", user?.Id, user?.Email);
            return Json(new { success = true, message = "Signoff removed.", resultsVisible, workspace });
        }

        [HttpGet]
        public async Task<IActionResult> Run(int id)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();

            var review = await _rule60.GetSavedRunAsync(id, user?.Email);
            if (review == null)
                return NotFound();

            if (!await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
            {
                TempData["Error"] = "You do not have access to this saved validation run.";
                return RedirectToAction("Index", "Dashboard");
            }

            if (!CanViewSavedRun(review, role))
            {
                TempData["Error"] = "Only analyst-signed validation results are available for review.";
                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.IsAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
            ViewBag.CanDownloadSavedRun = CanDownloadSavedRun(review, role);
            ViewBag.CanManageEngagement =
                string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);
            var clientDetail = await _systemDb.GetClientDetailAsync(review.ClientId, user, role);
            ViewBag.IsArchived = clientDetail?.IsArchived == true;
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForSavedRun(
                60, review.ClientId, clientDetail?.ValidationRuns, role, review.CurrentUserEngagementRole);
            ViewBag.CanOpenWorkspace =
                clientDetail?.IsArchived != true &&
                await _systemDb.CanAccessClientModuleAsync(review.ClientId, user, role) &&
                (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(review.CurrentUserEngagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            review.GeneratedSql = _rule60.GenerateSql(BuildRequestFromSummary(review.Summary!));
            return View(review);
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSql([FromBody] Rule41ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId > 0 && !await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule41SqlResult { Success = false, Error = "You cannot access this engagement." });

            return Json(RequireDataAnalystResult(() => new Rule41SqlResult { Success = true, Sql = _rule60.GenerateSql(request) }));
        }
        [HttpPost]
        public async Task<IActionResult> GenerateRScript([FromBody] Rule41ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId > 0 && !await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule41SqlResult { Success = false, Error = "You cannot access this engagement." });

            return Json(RequireDataAnalystResult(() => new Rule41SqlResult
            {
                Success = true,
                Sql = Rule60RScriptGenerator.Generate(request) + RScriptScaffold.BuildAutoExportFooter("Rule60")
            }));
        }

        [HttpGet]
        public async Task<IActionResult> DownloadExcel([FromQuery] int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            var bytes = _export.ExportRule60Excel(review.Summary!);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Rule60_CRSE_H16CRSE_Agreement_Run_{runId}.xlsx");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadCsv([FromQuery] int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            var bytes = BuildCsvExport(review.Summary!, false);
            return File(bytes, "text/csv", $"Rule60_CRSE_H16CRSE_Agreement_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadExceptionsCsv([FromQuery] int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            var bytes = BuildCsvExport(review.Summary!, true);
            return File(bytes, "text/csv", $"Rule60_Exceptions_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSql([FromQuery] int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            var req = BuildRequestFromSummary(review.Summary!);
            var bytes = _export.ExportSql(_rule60.GenerateSql(req));
            return File(bytes, "application/sql", $"Rule60_CRSE_H16CRSE_Agreement_Run_{runId}.sql");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedExcel([FromQuery] int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            var bytes = _export.ExportRule60Excel(review.Summary!);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Rule60_CRSE_H16CRSE_Agreement_Run_{runId}.xlsx");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedCsv([FromQuery] int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            var bytes = BuildCsvExport(review.Summary!, false);
            return File(bytes, "text/csv", $"Rule60_CRSE_H16CRSE_Agreement_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedExceptionsCsv([FromQuery] int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            var bytes = BuildCsvExport(review.Summary!, true);
            return File(bytes, "text/csv", $"Rule60_Exceptions_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedSql([FromQuery] int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            var req = BuildRequestFromSummary(review.Summary!);
            var bytes = _export.ExportSql(_rule60.GenerateSql(req));
            return File(bytes, "application/sql", $"Rule60_CRSE_H16CRSE_Agreement_Run_{runId}.sql");
        }

        [HttpPost]
        public async Task<IActionResult> AddSignoff(Rule41RunSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule60.GetSavedRunAsync(model.RunId, user?.Email);
            if (review == null) return NotFound();

            var clientDetail = await _systemDb.GetClientDetailAsync(review.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
            {
                TempData["Error"] = "Archived engagements are read-only.";
                return RedirectToAction(nameof(Run), new { id = model.RunId });
            }

            if (!await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
            {
                TempData["Error"] = "You do not have access.";
                return RedirectToAction("Index", "Dashboard");
            }

            if (!CanViewSavedRun(review, role))
            {
                TempData["Error"] = "Only analyst-signed results are available.";
                return RedirectToAction("Index", "Dashboard");
            }

            if (!review.IsCurrentRun)
            {
                TempData["Error"] = "History results are read-only.";
                return RedirectToAction(nameof(Run), new { id = model.RunId });
            }

            if (!review.CanCurrentUserSignOff)
            {
                TempData["Error"] = "Only the assigned data analyst, manager, or director can sign off.";
                return RedirectToAction(nameof(Run), new { id = model.RunId });
            }

            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
            {
                TempData["Error"] = "The assigned data analyst must sign off first.";
                return RedirectToAction(nameof(Run), new { id = model.RunId });
            }

            await _rule60.AddOrUpdateSignoffAsync(model.RunId, user!.Email!, model.Comment);
            await _audit.LogAsync("signoff_validation_run",
                $"{review.CurrentUserEngagementRole} signed off Rule 60 run {model.RunId}",
                user.Id, user.Email);

            TempData["Success"] = "Signoff saved.";
            return RedirectToAction(nameof(Run), new { id = model.RunId });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveSignoff(int runId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule60.GetSavedRunAsync(runId, user?.Email);
            if (review == null) return NotFound();

            var clientDetail = await _systemDb.GetClientDetailAsync(review.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
            {
                TempData["Error"] = "Archived engagements are read-only.";
                return RedirectToAction(nameof(Run), new { id = runId });
            }

            if (!await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
            {
                TempData["Error"] = "You do not have access.";
                return RedirectToAction("Index", "Dashboard");
            }

            if (!review.IsCurrentRun)
            {
                TempData["Error"] = "History results are read-only.";
                return RedirectToAction(nameof(Run), new { id = runId });
            }

            if (!review.CurrentUserHasSignedOff)
            {
                TempData["Error"] = "No signoff to remove.";
                return RedirectToAction(nameof(Run), new { id = runId });
            }

            await _rule60.RemoveSignoffAsync(runId, user!.Email!);
            await _audit.LogAsync("remove_validation_signoff",
                $"{review.CurrentUserEngagementRole} removed signoff for Rule 60 run {runId}",
                user?.Id, user?.Email);

            TempData["Success"] = "Signoff removed.";
            return RedirectToAction(nameof(Run), new { id = runId });
        }

        // Ã¢â€â‚¬Ã¢â€â‚¬ Private helpers Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬

        private static byte[] BuildExcelExport(Rule41ValidationSummary summary)
        {
            using var ms = new System.IO.MemoryStream();
            using var sw = new System.IO.StreamWriter(ms, System.Text.Encoding.UTF8);
            sw.WriteLine("HEMIS RULE 60 Ã¢â‚¬â€œ CRSE vs H16CRSE Agreement");
            sw.WriteLine($"Database: {summary.Database}  |  Timestamp: {summary.Timestamp}");
            sw.WriteLine();
            WriteReconcCsv(sw, summary.Reconc);
            sw.Flush();
            return ms.ToArray();
        }

        private static byte[] BuildCsvExport(Rule41ValidationSummary summary, bool exceptionsOnly)
        {
            using var ms = new System.IO.MemoryStream();
            using var sw = new System.IO.StreamWriter(ms, System.Text.Encoding.UTF8);
            sw.WriteLine($"\"HEMIS RULE 60 Ã¢â‚¬â€œ {(exceptionsOnly ? "Exceptions" : "All Results")}\"");
            sw.WriteLine($"\"Database\",\"{summary.Database}\"");
            sw.WriteLine($"\"Timestamp\",\"{summary.Timestamp}\"");
            sw.WriteLine();
            WriteReconcCsv(sw, summary.Reconc, exceptionsOnly);
            sw.Flush();
            return ms.ToArray();
        }

        private static void WriteReconcCsv(System.IO.StreamWriter sw, Rule41ReconciliationSummary reconc, bool exceptionsOnly = false)
        {
            sw.WriteLine($"\"CRSE vs H16CRSE Reconciliation\"");
            sw.WriteLine($"\"CRSE Table\",\"{reconc.StudTable}\"  \"H16CRSE Table\",\"{reconc.AuditTable}\"");
            sw.WriteLine($"\"Total\",{reconc.TotalCount}  \"Agree\",{reconc.AgreeCount}  \"Disagree\",{reconc.DisagreeCount}  \"Missing\",{reconc.MissingCount}  \"Exception Rate\",{reconc.ExceptionRate:0.00}%");

            var labels = reconc.Pairs.Select(p => p.Label).ToList();
            sw.WriteLine("\"Row_No\",\"CRSE_Ref\"," +
                string.Join(",", labels.Select(l => $"\"CRSE_{l}\",\"H16CRSE_{l}\",\"MATCH_{l}\"")) +
                ",\"Overall_Result\",\"Disagree_Detail\"");

            var rows = exceptionsOnly ? reconc.ExceptionRows : reconc.ExceptionRows.Concat(reconc.Rows);
            foreach (var row in rows)
            {
                var line = new System.Text.StringBuilder();
                line.Append($"{row.RowNumber},\"{row.StudentRef}\",");
                foreach (var lbl in labels)
                {
                    if (row.Fields.TryGetValue(lbl, out var fv))
                        line.Append($"\"{fv.StudValue}\",\"{fv.AuditValue}\",\"{fv.Match}\",");
                    else
                        line.Append("\"Ã¢â‚¬â€\",\"Ã¢â‚¬â€\",\"Ã¢â‚¬â€\",");
                }
                line.Append($"\"{row.OverallResult}\",\"{row.DisagreeDetail.Replace("\"", "\"\"")}\"");
                sw.WriteLine(line.ToString());
            }
        }

        private static byte[] BuildXlsxExport(Rule41ValidationSummary summary)
        {
            using var workbook = new XLWorkbook();

            var summarySheet = workbook.Worksheets.Add("Summary");
            summarySheet.Cell(1, 1).Value = "HEMIS RULE 60 - CRSE vs H16CRSE Agreement";
            summarySheet.Range(1, 1, 1, 2).Merge();
            summarySheet.Cell(1, 1).Style.Font.Bold = true;
            summarySheet.Cell(1, 1).Style.Font.FontSize = 14;

            var summaryRows = new (string Label, string Value)[]
            {
                ("Database", summary.Database),
                ("Timestamp", summary.Timestamp),
                ("Status", summary.Status),
                ("CRSE Table", summary.Reconc.StudTable),
                ("H16CRSE Table", summary.Reconc.AuditTable),
                ("CRSE Join Key", summary.Reconc.StudKey),
                ("H16CRSE Join Key", summary.Reconc.AuditKey),
                ("Total Count", summary.Reconc.TotalCount.ToString()),
                ("Agree Count", summary.Reconc.AgreeCount.ToString()),
                ("Disagree Count", summary.Reconc.DisagreeCount.ToString()),
                ("Missing Count", summary.Reconc.MissingCount.ToString()),
                ("Exception Rate", $"{summary.Reconc.ExceptionRate:F2}%")
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

            var mappingStartRow = summaryRowIndex + 2;
            summarySheet.Cell(mappingStartRow, 1).Value = "Field Mappings";
            summarySheet.Cell(mappingStartRow, 1).Style.Font.Bold = true;
            summarySheet.Cell(mappingStartRow + 1, 1).Value = "Label";
            summarySheet.Cell(mappingStartRow + 1, 2).Value = "CRSE Column";
            summarySheet.Cell(mappingStartRow + 1, 3).Value = "H16CRSE Column";
            summarySheet.Range(mappingStartRow + 1, 1, mappingStartRow + 1, 3).Style.Font.Bold = true;
            summarySheet.Range(mappingStartRow + 1, 1, mappingStartRow + 1, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");

            var mappingRowIndex = mappingStartRow + 2;
            foreach (var pair in summary.Reconc.Pairs)
            {
                summarySheet.Cell(mappingRowIndex, 1).Value = pair.Label;
                summarySheet.Cell(mappingRowIndex, 2).Value = pair.StudCol;
                summarySheet.Cell(mappingRowIndex, 3).Value = pair.AuditCol;
                mappingRowIndex++;
            }

            summarySheet.Columns().AdjustToContents();

            WriteReconciliationWorksheet(
                workbook,
                "All Results",
                summary.Reconc,
                summary.Reconc.ExceptionRows.Concat(summary.Reconc.Rows));
            WriteReconciliationWorksheet(
                workbook,
                "Exceptions",
                summary.Reconc,
                summary.Reconc.ExceptionRows);

            using var ms = new System.IO.MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }

        private static void WriteReconciliationWorksheet(
            XLWorkbook workbook,
            string sheetName,
            Rule41ReconciliationSummary reconc,
            IEnumerable<Rule41ReconcRow> rows)
        {
            var worksheet = workbook.Worksheets.Add(sheetName);
            worksheet.Cell(1, 1).Value = "CRSE vs H16CRSE Reconciliation";
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontSize = 13;

            worksheet.Cell(2, 1).Value = "CRSE Table";
            worksheet.Cell(2, 2).Value = reconc.StudTable;
            worksheet.Cell(2, 3).Value = "H16CRSE Table";
            worksheet.Cell(2, 4).Value = reconc.AuditTable;

            worksheet.Cell(3, 1).Value = "Total";
            worksheet.Cell(3, 2).Value = reconc.TotalCount;
            worksheet.Cell(3, 3).Value = "Agree";
            worksheet.Cell(3, 4).Value = reconc.AgreeCount;
            worksheet.Cell(3, 5).Value = "Disagree";
            worksheet.Cell(3, 6).Value = reconc.DisagreeCount;
            worksheet.Cell(3, 7).Value = "Missing";
            worksheet.Cell(3, 8).Value = reconc.MissingCount;
            worksheet.Cell(3, 9).Value = "Exception Rate";
            worksheet.Cell(3, 10).Value = $"{reconc.ExceptionRate:F2}%";

            var headerRow = 5;
            var columnIndex = 1;
            worksheet.Cell(headerRow, columnIndex++).Value = "Row No";
            worksheet.Cell(headerRow, columnIndex++).Value = "CRSE Ref";

            foreach (var pair in reconc.Pairs)
            {
                worksheet.Cell(headerRow, columnIndex++).Value = $"CRSE_{pair.Label}";
                worksheet.Cell(headerRow, columnIndex++).Value = $"H16CRSE_{pair.Label}";
                worksheet.Cell(headerRow, columnIndex++).Value = $"MATCH_{pair.Label}";
            }

            worksheet.Cell(headerRow, columnIndex++).Value = "Overall Result";
            worksheet.Cell(headerRow, columnIndex).Value = "Disagree Detail";

            worksheet.Range(headerRow, 1, headerRow, columnIndex).Style.Font.Bold = true;
            worksheet.Range(headerRow, 1, headerRow, columnIndex).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");

            var rowIndex = headerRow + 1;
            foreach (var row in rows)
            {
                var cellIndex = 1;
                worksheet.Cell(rowIndex, cellIndex++).Value = row.RowNumber;
                worksheet.Cell(rowIndex, cellIndex++).Value = row.StudentRef;

                foreach (var pair in reconc.Pairs)
                {
                    if (row.Fields.TryGetValue(pair.Label, out var field))
                    {
                        worksheet.Cell(rowIndex, cellIndex++).Value = field.StudValue;
                        worksheet.Cell(rowIndex, cellIndex++).Value = field.AuditValue;
                        worksheet.Cell(rowIndex, cellIndex++).Value = field.Match;
                    }
                    else
                    {
                        worksheet.Cell(rowIndex, cellIndex++).Value = "-";
                        worksheet.Cell(rowIndex, cellIndex++).Value = "-";
                        worksheet.Cell(rowIndex, cellIndex++).Value = "-";
                    }
                }

                worksheet.Cell(rowIndex, cellIndex++).Value = row.OverallResult;
                worksheet.Cell(rowIndex, cellIndex).Value = row.DisagreeDetail;
                rowIndex++;
            }

            worksheet.SheetView.FreezeRows(headerRow);
            worksheet.Range(headerRow, 1, Math.Max(headerRow, rowIndex - 1), columnIndex).SetAutoFilter();
            worksheet.Columns().AdjustToContents();
        }

        private static Rule41ValidationRequest BuildRequestFromSummary(Rule41ValidationSummary s) =>
            new()
            {
                Database   = s.Database,
                StudTable  = s.StudTable,
                AuditTable = s.AuditTable,
                StudKey    = s.StudKey,
                AuditKey   = s.AuditKey,
                Pairs      = s.Reconc.Pairs
            };

        private async Task<Rule41RunReviewViewModel?> LoadAuthorizedSavedRunAsync(int runId, bool requireDownloadAccess)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule60.GetSavedRunAsync(runId, user?.Email, includeFullResults: requireDownloadAccess);
            if (review == null)
            {
                TempData["Error"] = "Saved validation run was not found.";
                return null;
            }

            if (!await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
            {
                TempData["Error"] = "You do not have access to this saved validation run.";
                return null;
            }

            if (!CanViewSavedRun(review, role))
            {
                TempData["Error"] = "Only analyst-signed results are available for review.";
                return null;
            }

            if (requireDownloadAccess && !CanDownloadSavedRun(review, role))
            {
                TempData["Error"] = "The assigned data analyst must sign off before other assigned users can download this run.";
                return null;
            }

            return review;
        }

        private static bool CanDownloadSavedRun(Rule41RunReviewViewModel review, string systemRole) =>
            ValidationRunAccessPolicy.CanDownloadSignedResults(systemRole, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff);

        private static bool CanViewSavedRun(Rule41RunReviewViewModel review, string systemRole) =>
            ValidationRunAccessPolicy.CanViewSignedResults(systemRole, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff);

        private static bool CanViewWorkspaceResults(string role, Rule41WorkspaceStateViewModel? workspace)
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
                return new { success = false, error = "Only the assigned data analyst can configure or run Rule 60." };
            var result = await action();
            return result!;
        }

        private object RequireDataAnalystResult(Func<Rule41SqlResult> factory)
        {
            var user = _users.GetUserAsync(User).GetAwaiter().GetResult();
            var role = GetCurrentSystemRoleAsync(user).GetAwaiter().GetResult();
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return new { success = false, error = "Only the assigned data analyst can generate the SQL script." };
            return factory();
        }
    }
}
