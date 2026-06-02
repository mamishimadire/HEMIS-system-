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
    public class Rule54Controller : Controller
    {
        private readonly IRule54Service _rule54;
        private readonly IExportService _export;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;

        public Rule54Controller(
            IRule54Service rule54,
            IExportService export,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb)
        {
            _rule54 = rule54;
            _export = export;
            _audit = audit;
            _users = users;
            _systemDb = systemDb;
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
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForWorkspace(54, clientId);
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

            var workspace = await _rule54.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);

            if (workspace != null)
                workspace.ResultsVisible = resultsVisible;

            if (workspace != null && !resultsVisible) workspace.Summary = null;

            return Json(new { success = true, hasWorkspace = workspace != null, resultsVisible, workspace });
        }

        [HttpGet]
        public async Task<IActionResult> Run(int id)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();
            var review = await _rule54.GetSavedRunAsync(id, user?.Email);
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
            var isArchived = clientDetail?.IsArchived == true;
            ViewBag.IsArchived = isArchived;
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForSavedRun(
                54, review.ClientId, clientDetail?.ValidationRuns, role, review.CurrentUserEngagementRole);
            ViewBag.CanOpenWorkspace =
                !isArchived &&
                await _systemDb.CanAccessClientModuleAsync(review.ClientId, user, role) &&
                (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(review.CurrentUserEngagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            review.GeneratedSql = _rule54.GenerateSql(new Rule54ValidationRequest
            {
                ClientId        = review.ClientId,
                Database        = review.Summary.Database,
                CredTable       = review.Summary.CredTable,
                CredIdCol       = review.Summary.CredIdCol,
                CredCourseCol   = review.Summary.CredCourseCol,
                CredCreditCol   = review.Summary.CredCreditCol,
                CredResearch1Col = review.Summary.CredResearch1Col,
                QualTable       = review.Summary.QualTable,
                QualIdCol       = review.Summary.QualIdCol,
                QualNameCol     = review.Summary.QualNameCol,
                PqmTable        = review.Summary.PqmTable,
                PqmNameCol      = review.Summary.PqmNameCol,
                PqmResearch1Col = review.Summary.PqmResearch1Col
            });
            return View(review);
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule54.GetDatabasesAsync(model.Server, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule54.GetTablesAsync(model.Server, model.Database, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetColumns([FromBody] Rule54GetColumnsRequest model) =>
            Json(await RequireDataAnalystAsync(async () =>
                await _rule54.GetColumnsAsync(model.Server, model.Database, model.Driver, model.TableName, model.TableRole)));

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] Rule54VerifyRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule54.VerifyDataAsync(request)));

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] Rule54ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new Rule54ValidationSummary { Success = false, Error = "Select an approved engagement before running validation." });

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule54ValidationSummary { Success = false, Error = "You cannot access this engagement." });

            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new Rule54ValidationSummary { Success = false, Error = "Only the assigned data analyst can run Rule 54." });
            }

            async Task<Rule54ValidationSummary> ExecuteAsync(IRule54Service svc, IAuditLogService auditSvc)
            {
                var result = await svc.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);
                if (result.Success)
                {
                    await auditSvc.LogAsync(
                        "run_validation",
                        $"Rule 54 on client {request.ClientId}: {result.Status} ({result.FailCount} exceptions), run {result.SavedRunId}",
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
                    "Rule 54 validation",
                    async (sp, ct) => await ExecuteAsync(
                        sp.GetRequiredService<IRule54Service>(),
                        sp.GetRequiredService<IAuditLogService>()));
            }

            return Json(await ExecuteAsync(_rule54, _audit));
        }

        [HttpPost]
        public async Task<IActionResult> BeginWorkspaceEdit([FromBody] Rule54ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (!await CanEditWorkspaceAsync(request.ClientId, user, role))
                return Json(new Rule54WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can edit a saved workspace." });

            if (!request.RunId.HasValue || request.RunId.Value <= 0)
                return Json(new Rule54WorkspaceSaveResult { Success = false, Error = "Select a saved run before editing the workspace." });

            var result = await _rule54.BeginWorkspaceEditAsync(request.RunId.Value, user!.Email!, user.FullName);
            if (result.Success)
            {
                await _audit.LogAsync("workspace_edit_started",
                    $"DataAnalyst started editing Rule 54 run {request.RunId.Value}. Existing signoffs were cleared.",
                    user?.Id, user?.Email);
            }
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] Rule54ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (!await CanEditWorkspaceAsync(request.ClientId, user, role))
                return Json(new Rule54WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can save a workspace." });

            var result = await _rule54.SaveWorkspaceAsync(request, user!.Email!, user.FullName);
            if (result.Success)
            {
                await _audit.LogAsync("save_validation_workspace",
                    $"DataAnalyst saved Rule 54 workspace for client {request.ClientId}. Signoffs cleared: {result.ClearedSignoffCount ?? 0}",
                    user?.Id, user?.Email);
            }
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SignOffWorkspace([FromBody] Rule54WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0)
                return Json(new { success = false, error = "Select an engagement before signing off." });

            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can sign off the workspace." });

            if (!model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Run validation first so the workspace is saved." });

            var review = await _rule54.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved validation run could not be found for this engagement." });

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
                return Json(new { success = false, error = "Archived engagements are read-only. Signoff is disabled." });

            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
                return Json(new { success = false, error = "The assigned data analyst must sign off before this review can be completed." });

            try
            {
                await _rule54.AddOrUpdateSignoffAsync(model.RunId.Value, user!.Email!, model.Comment);
                await _audit.LogAsync("signoff_validation_run",
                    $"Signed off Rule 54 run {model.RunId.Value} from module workspace",
                    user.Id, user.Email);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }

            var workspace = await _rule54.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            return Json(new { success = true, message = "Signoff saved.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveWorkspaceSignoff([FromBody] Rule54WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0 || !model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Select a saved run before removing signoff." });

            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can remove signoff." });

            var review = await _rule54.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved validation run could not be found for this engagement." });

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
                return Json(new { success = false, error = "Archived engagements are read-only. Signoff removal is disabled." });

            if (!review.CurrentUserHasSignedOff)
                return Json(new { success = false, error = "There is no signoff for your assigned engagement role to remove." });

            try
            {
                await _rule54.RemoveSignoffAsync(model.RunId.Value, user!.Email!);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }

            var workspace = await _rule54.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            var reopenedRunId = workspace?.RunId;
            var preservedHistory = reopenedRunId.HasValue && reopenedRunId.Value != model.RunId.Value;
            var message = preservedHistory
                ? $"Signoff removed. Run #{model.RunId.Value} moved to history and Run #{reopenedRunId.Value} is now the current workspace."
                : "Signoff removed.";
            await _audit.LogAsync("remove_validation_signoff",
                preservedHistory
                    ? $"{review.CurrentUserEngagementRole} removed signoff for Rule 54 run {model.RunId.Value}. Historical snapshot preserved; new current run {reopenedRunId.Value} created."
                    : $"{review.CurrentUserEngagementRole} removed signoff for Rule 54 run {model.RunId.Value} from module workspace",
                user.Id, user.Email);
            return Json(new { success = true, message, resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSql([FromBody] Rule54ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId > 0 && !await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new SqlResult { Success = false, Error = "You cannot access this engagement." });

            return Json(RequireDataAnalystResult(() => new SqlResult { Success = true, Sql = _rule54.GenerateSql(request) }));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSignoff(Rule54RunSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule54.GetSavedRunAsync(model.RunId, user?.Email);
            if (review == null) return NotFound();

            var clientDetail = await _systemDb.GetClientDetailAsync(review.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
            {
                TempData["Error"] = "Archived engagements are read-only. Signoff is disabled.";
                return RedirectToAction(nameof(Run), new { id = model.RunId });
            }

            if (!await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
            {
                TempData["Error"] = "You do not have access to sign off this run.";
                return RedirectToAction("Index", "Dashboard");
            }

            if (!CanViewSavedRun(review, role))
            {
                TempData["Error"] = "Only analyst-signed validation results are available for review.";
                return RedirectToAction("Index", "Dashboard");
            }

            if (!review.IsCurrentRun)
            {
                TempData["Error"] = "History results are read-only. Signoff is only available on the current run.";
                return RedirectToAction(nameof(Run), new { id = model.RunId });
            }

            if (!review.CanCurrentUserSignOff)
            {
                TempData["Error"] = "Only the assigned data analyst, manager, or director can sign off this run.";
                return RedirectToAction(nameof(Run), new { id = model.RunId });
            }

            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
            {
                TempData["Error"] = "The assigned data analyst must sign off before this review can be completed.";
                return RedirectToAction(nameof(Run), new { id = model.RunId });
            }

            await _rule54.AddOrUpdateSignoffAsync(model.RunId, user!.Email!, model.Comment);
            await _audit.LogAsync("signoff_validation_run",
                $"{review.CurrentUserEngagementRole} signed off Rule 54 run {model.RunId}",
                user.Id, user.Email);

            TempData["Success"] = "Signoff saved.";
            return RedirectToAction(nameof(Run), new { id = model.RunId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveSignoff(int runId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule54.GetSavedRunAsync(runId, user?.Email);
            if (review == null) return NotFound();

            var clientDetail = await _systemDb.GetClientDetailAsync(review.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
            {
                TempData["Error"] = "Archived engagements are read-only. Signoff removal is disabled.";
                return RedirectToAction(nameof(Run), new { id = runId });
            }

            if (!await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
            {
                TempData["Error"] = "You do not have access to remove this signoff.";
                return RedirectToAction("Index", "Dashboard");
            }

            if (!review.IsCurrentRun)
            {
                TempData["Error"] = "History results are read-only. Signoff cannot be removed from a history run.";
                return RedirectToAction(nameof(Run), new { id = runId });
            }

            if (!review.CurrentUserHasSignedOff)
            {
                TempData["Error"] = "There is no signoff for your assigned engagement role to remove.";
                return RedirectToAction(nameof(Run), new { id = runId });
            }

            await _rule54.RemoveSignoffAsync(runId, user!.Email!);
            var workspace = await _rule54.GetCurrentWorkspaceStateAsync(review.ClientId, user?.Email);
            var redirectRunId = workspace?.RunId ?? runId;
            var preservedHistory = workspace?.RunId.HasValue == true && workspace.RunId.Value != runId;
            await _audit.LogAsync("remove_validation_signoff",
                preservedHistory
                    ? $"{review.CurrentUserEngagementRole} removed signoff for Rule 54 run {runId}. Historical snapshot preserved; new current run {redirectRunId} created."
                    : $"{review.CurrentUserEngagementRole} removed signoff for Rule 54 run {runId}",
                user.Id, user.Email);

            TempData["Success"] = preservedHistory
                ? $"Signoff removed. Run #{runId} moved to history and Run #{redirectRunId} is now current."
                : "Signoff removed.";
            return RedirectToAction(nameof(Run), new { id = redirectRunId });
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedExcel(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            var bytes = _export.ExportRule54Excel(review.Summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Rule54_CRED_QUAL_PQM_Validation_Run_{runId}.xlsx");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedCsv(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            var bytes = _export.ExportRule54Csv(review.Summary, false);
            return File(bytes, "text/csv", $"Rule54_Validation_Results_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedExceptionsCsv(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            var bytes = _export.ExportRule54Csv(review.Summary, true);
            return File(bytes, "text/csv", $"Rule54_CRED_QUAL_PQM_Exceptions_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedSql(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            var request = new Rule54ValidationRequest
            {
                ClientId        = review.ClientId,
                Database        = review.Summary.Database,
                CredTable       = review.Summary.CredTable,
                CredIdCol       = review.Summary.CredIdCol,
                CredCourseCol   = review.Summary.CredCourseCol,
                CredCreditCol   = review.Summary.CredCreditCol,
                CredResearch1Col = review.Summary.CredResearch1Col,
                QualTable       = review.Summary.QualTable,
                QualIdCol       = review.Summary.QualIdCol,
                QualNameCol     = review.Summary.QualNameCol,
                PqmTable        = review.Summary.PqmTable,
                PqmNameCol      = review.Summary.PqmNameCol,
                PqmResearch1Col = review.Summary.PqmResearch1Col
            };
            var bytes = _export.ExportSql(_rule54.GenerateSql(request));
            return File(bytes, "application/sql", $"Rule54_CRED_QUAL_PQM_Validation_Run_{runId}.sql");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadExcel([FromBody] Rule54ValidationSummary summary)
        {
            summary = await ResolveExportSummaryAsync(summary);
            var bytes = _export.ExportRule54Excel(summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Rule54_CRED_QUAL_PQM_Validation_{Ts()}.xlsx");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadCsv([FromBody] Rule54ValidationSummary summary)
        {
            summary = await ResolveExportSummaryAsync(summary);
            var bytes = _export.ExportRule54Csv(summary, false);
            return File(bytes, "text/csv", $"Rule54_Validation_Results_{Ts()}.csv");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadExceptionsCsv([FromBody] Rule54ValidationSummary summary)
        {
            summary = await ResolveExportSummaryAsync(summary);
            var bytes = _export.ExportRule54Csv(summary, true);
            return File(bytes, "text/csv", $"Rule54_CRED_QUAL_PQM_Exceptions_{Ts()}.csv");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadSql([FromBody] Rule54ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, error = "Only the assigned data analyst can download the SQL script." });
            var bytes = _export.ExportSql(_rule54.GenerateSql(request));
            return File(bytes, "application/sql", $"Rule54_CRED_QUAL_PQM_Validation_{Ts()}.sql");
        }

        // â”€â”€ Private helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private async Task<Rule54RunReviewViewModel?> LoadAuthorizedSavedRunAsync(int runId, bool requireDownloadAccess)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule54.GetSavedRunAsync(runId, user?.Email);
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
                TempData["Error"] = "Only analyst-signed validation results are available for review.";
                return null;
            }
            if (requireDownloadAccess && !CanDownloadSavedRun(review, role))
            {
                TempData["Error"] = "The assigned data analyst must sign off before other assigned users can download this run.";
                return null;
            }
            return review;
        }

        private static bool CanDownloadSavedRun(Rule54RunReviewViewModel review, string systemRole)
            => ValidationRunAccessPolicy.CanDownloadSignedResults(systemRole, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff);

        private static bool CanViewSavedRun(Rule54RunReviewViewModel review, string systemRole)
            => ValidationRunAccessPolicy.CanViewSignedResults(systemRole, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff);

        private static bool CanViewWorkspaceResults(string role, Rule54WorkspaceStateViewModel? workspace)
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
            if (user == null) return false;
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase)) return false;
            if (clientId <= 0) return false;
            if (!await _systemDb.CanAccessClientResultsAsync(clientId, user, role)) return false;
            var engagementRole = await _systemDb.GetEngagementRoleAsync(clientId, user, role);
            return ValidationRunAccessPolicy.IsAssignedDataAnalyst(engagementRole);
        }

        private async Task<Rule54ValidationSummary> ResolveExportSummaryAsync(Rule54ValidationSummary summary)
        {
            var user = await _users.GetUserAsync(User);
            if (summary.SavedRunId is int savedRunId && savedRunId > 0)
            {
                var review = await _rule54.GetSavedRunAsync(savedRunId, user?.Email);
                if (review?.Summary != null) return review.Summary;
            }
            if (summary.ClientId > 0)
            {
                var workspace = await _rule54.GetCurrentWorkspaceStateAsync(summary.ClientId, user?.Email);
                if (workspace?.RunId is int workspaceRunId && workspaceRunId > 0)
                {
                    var review = await _rule54.GetSavedRunAsync(workspaceRunId, user?.Email);
                    if (review?.Summary != null) return review.Summary;
                }
            }
            return summary;
        }

        private async Task<object> RequireDataAnalystAsync<T>(Func<Task<T>> action)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return new { success = false, error = "Only the assigned data analyst can configure or run Rule 54." };
            return await action();
        }

        private object RequireDataAnalystResult(Func<SqlResult> factory)
        {
            var user = _users.GetUserAsync(User).GetAwaiter().GetResult();
            var role = GetCurrentSystemRoleAsync(user).GetAwaiter().GetResult();
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return new { success = false, error = "Only the assigned data analyst can generate the SQL script." };
            return factory();
        }

        private static string Ts() => DateTime.Now.ToString("yyyyMMdd_HHmmss");
    }
}
