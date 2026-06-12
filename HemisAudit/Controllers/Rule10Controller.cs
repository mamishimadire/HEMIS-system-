using HemisAudit.Helpers;
using HemisAudit.Models;
using HemisAudit.Services;
using HemisAudit.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HemisAudit.Controllers
{
    [Authorize]
    public class Rule10Controller : Controller
    {
        private readonly IRule10Service _rule10;
        private readonly IExportService _export;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;

        public Rule10Controller(
            IRule10Service rule10,
            IExportService export,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb)
        {
            _rule10 = rule10;
            _export = export;
            _audit = audit;
            _users = users;
            _systemDb = systemDb;
        }

        public async Task<IActionResult> Index(int clientId = 0)
        {
            var ruleNumber = ResolveRuleNumber();
            var rule = IntegrityRuleCatalog.Get(ruleNumber);
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
            ViewBag.RuleNumber = ruleNumber;
            ViewBag.RuleMetadata = rule;
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForWorkspace(ruleNumber, clientId);
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetWorkspaceState(int clientId)
        {
            var ruleNumber = ResolveRuleNumber();
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

            var workspace = await _rule10.GetCurrentWorkspaceStateAsync(ruleNumber, clientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);

            if (workspace != null)
                workspace.ResultsVisible = resultsVisible;

            if (workspace != null && !resultsVisible) workspace.Summary = null;

            return Json(new
            {
                success = true,
                hasWorkspace = workspace != null,
                resultsVisible,
                workspace
            });
        }

        [HttpGet]
        public async Task<IActionResult> Run(int id)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();

            var review = await _rule10.GetSavedRunAsync(id, user?.Email);
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
                review.RuleNumber,
                review.ClientId,
                clientDetail?.ValidationRuns,
                role,
                review.CurrentUserEngagementRole);
            ViewBag.RuleNumber = review.RuleNumber;
            ViewBag.RuleMetadata = IntegrityRuleCatalog.Get(review.RuleNumber);
            ViewBag.CanOpenWorkspace =
                !isArchived &&
                await _systemDb.CanAccessClientModuleAsync(review.ClientId, user, role) &&
                (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(review.CurrentUserEngagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            review.GeneratedSql = await _rule10.GenerateSqlAsync(new Rule10ValidationRequest
            {
                ClientId = review.ClientId,
                RuleNumber = review.RuleNumber,
                Server = review.SourceServer,
                Database = review.Summary.Database,
                QualTable = review.Summary.QualTable,
                StudTable = review.Summary.StudTable,
                CregTable = review.Summary.CregTable,
                CrseTable = review.Summary.CrseTable,
                QualColumn = review.Summary.QualColumn,
                StudColumn = review.Summary.StudColumn,
                CregColumn = review.Summary.CregColumn,
                CrseColumn = review.Summary.CrseColumn,
                RuleParameterJson = review.Summary.RuleParameterJson,
                Rule10JoinConfigJson = review.Summary.Rule10JoinConfigJson
            });

            return View(review);
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule10.GetDatabasesAsync(model.Server, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule10.GetTablesAsync(model.Server, model.Database, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetColumns([FromBody] Rule10GetColumnsRequest model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule10.GetColumnsAsync(model.Server, model.Database, model.Driver, model.TableName)));

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] Rule10VerifyRequest request)
        {
            request.RuleNumber = ResolveRuleNumber(request.RuleNumber);
            return Json(await RequireDataAnalystAsync(async () => await _rule10.VerifyTablesAsync(request)));
        }

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] Rule10ValidationRequest request)
        {
            request.RuleNumber = ResolveRuleNumber(request.RuleNumber);
            var rule = IntegrityRuleCatalog.Get(request.RuleNumber);
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
            {
                return Json(new Rule10ValidationSummary
                {
                    Success = false,
                    Error = "Select an approved engagement before running validation."
                });
            }

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
            {
                return Json(new Rule10ValidationSummary
                {
                    Success = false,
                    Error = "You cannot access this engagement."
                });
            }

            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new Rule10ValidationSummary
                {
                    Success = false,
                    Error = $"Only the assigned data analyst can run {rule.RuleLabel}."
                });
            }

            async Task<Rule10ValidationSummary> ExecuteValidationAsync(IRule10Service ruleService, IAuditLogService auditService)
            {
                var result = await ruleService.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);
                if (result.Success)
                {
                    await auditService.LogAsync(
                        "run_validation",
                        $"{rule.RuleLabel} on client {request.ClientId}: {result.Status} ({result.FailedChecks} failed checks, {result.TotalIssues} total issues). Validation completed but is not saved until Save Workspace is clicked.",
                        user?.Id,
                        user?.Email);
                }

                return result;
            }

            if (ValidationOperationHttpHelper.IsAsyncRequested(Request))
            {
                return ValidationOperationHttpHelper.Queue(
                    this,
                    HttpContext.RequestServices.GetRequiredService<IValidationOperationService>(),
                    ValidationOperationHttpHelper.ResolveOwnerKey(User),
                    $"{rule.RuleLabel} validation",
                    async (sp, ct) => await ExecuteValidationAsync(
                        sp.GetRequiredService<IRule10Service>(),
                        sp.GetRequiredService<IAuditLogService>()));
            }

            var result = await ExecuteValidationAsync(_rule10, _audit);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> BeginWorkspaceEdit([FromBody] Rule10ValidationRequest request)
        {
            request.RuleNumber = ResolveRuleNumber(request.RuleNumber);
            var rule = IntegrityRuleCatalog.Get(request.RuleNumber);
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (!await CanEditWorkspaceAsync(request.ClientId, user, role))
            {
                return Json(new Rule10WorkspaceSaveResult
                {
                    Success = false,
                    Error = "Only the assigned data analyst can edit a saved workspace."
                });
            }

            if (!request.RunId.HasValue || request.RunId.Value <= 0)
            {
                return Json(new Rule10WorkspaceSaveResult
                {
                    Success = false,
                    Error = "Select a saved run before editing the workspace."
                });
            }

            var result = await _rule10.BeginWorkspaceEditAsync(request.RuleNumber, request.RunId.Value, user!.Email!, user.FullName);
            if (result.Success)
            {
                await _audit.LogAsync(
                    "workspace_edit_started",
                    $"DataAnalyst started editing {rule.RuleLabel} run {request.RunId.Value}. A new validation must be saved before signoff.",
                    user?.Id,
                    user?.Email);
            }

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] Rule10ValidationRequest request)
        {
            request.RuleNumber = ResolveRuleNumber(request.RuleNumber);
            var rule = IntegrityRuleCatalog.Get(request.RuleNumber);
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (!await CanEditWorkspaceAsync(request.ClientId, user, role))
            {
                return Json(new Rule10WorkspaceSaveResult
                {
                    Success = false,
                    Error = "Only the assigned data analyst can save a workspace."
                });
            }

            var result = await _rule10.SaveWorkspaceAsync(request, user!.Email!, user.FullName);
            if (result.Success)
            {
                await _audit.LogAsync(
                    "save_validation_workspace",
                    $"DataAnalyst saved {rule.RuleLabel} workspace for client {request.ClientId}. Current run: {result.Workspace?.RunId}",
                    user?.Id,
                    user?.Email);
            }

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SignOffWorkspace([FromBody] Rule10WorkspaceSignoffInputModel model)
        {
            var ruleNumber = ResolveRuleNumber();
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0)
                return Json(new { success = false, error = "Select an engagement before signing off." });

            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can sign off the workspace." });

            if (!model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Run the validation first so the workspace is saved." });

            var review = await _rule10.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved validation run could not be found for this engagement." });

            if (!review.IsCurrentRun)
                return Json(new { success = false, error = "History results are read-only. Signoff is only available on the current run." });

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
                return Json(new { success = false, error = "Archived engagements are read-only. Signoff is disabled." });

            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
            {
                return Json(new { success = false, error = "The assigned data analyst must sign off before this review can be completed." });
            }

            try
            {
                await _rule10.AddOrUpdateSignoffAsync(model.RunId.Value, user!.Email!, model.Comment);
                await _audit.LogAsync(
                    "signoff_validation_run",
                    $"Rules 1-10 signoff saved for run {model.RunId.Value} from module workspace",
                    user.Id,
                    user.Email);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }

            var workspace = await _rule10.GetCurrentWorkspaceStateAsync(ruleNumber, model.ClientId, user?.Email, includeSummary: false);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            return Json(new { success = true, message = "Signoff saved.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveWorkspaceSignoff([FromBody] Rule10WorkspaceSignoffInputModel model)
        {
            var ruleNumber = ResolveRuleNumber();
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0 || !model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Select a saved run before removing signoff." });

            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can remove signoff." });

            var review = await _rule10.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved validation run could not be found for this engagement." });

            if (!review.IsCurrentRun)
                return Json(new { success = false, error = "History results are read-only. Signoff can only be removed from the current run." });

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
                return Json(new { success = false, error = "Archived engagements are read-only. Signoff removal is disabled." });

            if (!review.CurrentUserHasSignedOff)
                return Json(new { success = false, error = "There is no signoff for your assigned engagement role to remove." });

            try
            {
                await _rule10.RemoveSignoffAsync(model.RunId.Value, user!.Email!);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }

            var workspace = await _rule10.GetCurrentWorkspaceStateAsync(ruleNumber, model.ClientId, user?.Email, includeSummary: false);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            var reopenedRunId = workspace?.RunId;
            var preservedHistory = reopenedRunId.HasValue && reopenedRunId.Value != model.RunId.Value;
            var reopenedRunLabel = reopenedRunId?.ToString() ?? model.RunId.Value.ToString();
            var message = preservedHistory
                ? $"Signoff removed. Run #{model.RunId.Value} moved to history and Run #{reopenedRunLabel} is now the current workspace."
                : "Signoff removed.";
            await _audit.LogAsync(
                "remove_validation_signoff",
                preservedHistory
                    ? $"{review.CurrentUserEngagementRole} removed signoff for Rules 1-10 run {model.RunId.Value} from module workspace. Historical snapshot preserved; new current run {reopenedRunLabel} created for continued review."
                    : $"{review.CurrentUserEngagementRole} removed signoff for Rules 1-10 run {model.RunId.Value} from module workspace",
                user?.Id,
                user?.Email);
            return Json(new { success = true, message, resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSql([FromBody] Rule10ValidationRequest request)
        {
            request.RuleNumber = ResolveRuleNumber(request.RuleNumber);
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId > 0 && !await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
            {
                return Json(new Rule10SqlResult
                {
                    Success = false,
                    Error = "You cannot access this engagement."
                });
            }

            return Json(await RequireDataAnalystAsync(async () =>
                new Rule10SqlResult
                {
                    Success = true,
                    Sql = await _rule10.GenerateSqlAsync(request)
                }));
        }
        [HttpPost]
        public async Task<IActionResult> GenerateRScript([FromBody] Rule10ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId > 0 && !await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule10SqlResult { Success = false, Error = "You cannot access this engagement." });

            return Json(await RequireDataAnalystAsync(async () => new Rule10SqlResult
            {
                Success = true,
                Sql = Rule10RScriptGenerator.Generate(request) + RScriptScaffold.BuildAutoExportFooter("Rule10")
            }));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSignoff(Rule10RunSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule10.GetSavedRunAsync(model.RunId, user?.Email);
            if (review == null)
                return NotFound();

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

            await _rule10.AddOrUpdateSignoffAsync(model.RunId, user!.Email!, model.Comment);
            await _audit.LogAsync(
                "signoff_validation_run",
                $"{review.CurrentUserEngagementRole} signed off Rules 1-10 run {model.RunId}",
                user.Id,
                user.Email);

            TempData["Success"] = "Signoff saved.";
            return RedirectToAction(nameof(Run), new { id = model.RunId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveSignoff(int runId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule10.GetSavedRunAsync(runId, user?.Email);
            if (review == null)
                return NotFound();

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

            await _rule10.RemoveSignoffAsync(runId, user!.Email!);
            var workspace = await _rule10.GetCurrentWorkspaceStateAsync(review.RuleNumber, review.ClientId, user?.Email, includeSummary: false);
            var redirectRunId = workspace?.RunId ?? runId;
            var preservedHistory = workspace?.RunId.HasValue == true && workspace.RunId.Value != runId;
            await _audit.LogAsync(
                "remove_validation_signoff",
                preservedHistory
                    ? $"{review.CurrentUserEngagementRole} removed signoff for Rules 1-10 run {runId}. Historical snapshot preserved; new current run {redirectRunId} created for continued review."
                    : $"{review.CurrentUserEngagementRole} removed signoff for Rules 1-10 run {runId}",
                user?.Id,
                user?.Email);

            TempData["Success"] = preservedHistory
                ? $"Signoff removed. Run #{runId} moved to history and Run #{redirectRunId} is now current."
                : "Signoff removed.";
            return RedirectToAction(nameof(Run), new { id = redirectRunId });
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedExcel(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null)
                return RedirectToAction(nameof(Run), new { id = runId });

            var exportSummary = await _rule10.GetExportSummaryAsync(new Rule10ValidationRequest
            {
                ClientId = review.ClientId,
                RunId = review.RunId,
                RuleNumber = review.RuleNumber,
                Server = review.SourceServer,
                Database = review.Summary.Database,
                Driver = "ODBC Driver 17 for SQL Server",
                QualTable = review.Summary.QualTable,
                StudTable = review.Summary.StudTable,
                CregTable = review.Summary.CregTable,
                CrseTable = review.Summary.CrseTable,
                QualColumn = review.Summary.QualColumn,
                StudColumn = review.Summary.StudColumn,
                CregColumn = review.Summary.CregColumn,
                CrseColumn = review.Summary.CrseColumn,
                RuleParameterJson = review.Summary.RuleParameterJson,
                Rule10JoinConfigJson = review.Summary.Rule10JoinConfigJson
            });

            var bytes = _export.ExportExcel(exportSummary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Rule{review.RuleNumber}_Integrity_Check_Run_{runId}.xlsx");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedCsv(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null)
                return RedirectToAction(nameof(Run), new { id = runId });

            var exportSummary = await _rule10.GetExportSummaryAsync(new Rule10ValidationRequest
            {
                ClientId = review.ClientId,
                RunId = review.RunId,
                RuleNumber = review.RuleNumber,
                Server = review.SourceServer,
                Database = review.Summary.Database,
                Driver = "ODBC Driver 17 for SQL Server",
                QualTable = review.Summary.QualTable,
                StudTable = review.Summary.StudTable,
                CregTable = review.Summary.CregTable,
                CrseTable = review.Summary.CrseTable,
                QualColumn = review.Summary.QualColumn,
                StudColumn = review.Summary.StudColumn,
                CregColumn = review.Summary.CregColumn,
                CrseColumn = review.Summary.CrseColumn,
                RuleParameterJson = review.Summary.RuleParameterJson,
                Rule10JoinConfigJson = review.Summary.Rule10JoinConfigJson
            });

            var bytes = _export.ExportCsv(exportSummary);
            return File(bytes, "text/csv", $"Rule{review.RuleNumber}_Integrity_Check_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedSql(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null)
                return RedirectToAction(nameof(Run), new { id = runId });

            var request = new Rule10ValidationRequest
            {
                ClientId = review.ClientId,
                RuleNumber = review.RuleNumber,
                Server = review.SourceServer,
                Database = review.Summary.Database,
                QualTable = review.Summary.QualTable,
                StudTable = review.Summary.StudTable,
                CregTable = review.Summary.CregTable,
                CrseTable = review.Summary.CrseTable,
                QualColumn = review.Summary.QualColumn,
                StudColumn = review.Summary.StudColumn,
                CregColumn = review.Summary.CregColumn,
                CrseColumn = review.Summary.CrseColumn,
                RuleParameterJson = review.Summary.RuleParameterJson,
                Rule10JoinConfigJson = review.Summary.Rule10JoinConfigJson
            };

            var bytes = _export.ExportSql(await _rule10.GenerateSqlAsync(request));
            return File(bytes, "application/sql", $"Rule{review.RuleNumber}_Integrity_Check_{runId}.sql");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadExcel([FromBody] Rule10ValidationSummary summary)
        {
            summary = await ResolveExportSummaryAsync(summary);
            var bytes = _export.ExportExcel(summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Rule{summary.RuleNumber}_Integrity_Check_{Ts()}.xlsx");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadCsv([FromBody] Rule10ValidationSummary summary)
        {
            summary = await ResolveExportSummaryAsync(summary);
            var bytes = _export.ExportCsv(summary);
            return File(bytes, "text/csv", $"Rule{summary.RuleNumber}_Integrity_Check_{Ts()}.csv");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadSql([FromBody] Rule10ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, error = "Only the assigned data analyst can download the SQL script." });

            var bytes = _export.ExportSql(await _rule10.GenerateSqlAsync(request));
            return File(bytes, "application/sql", $"Rule10_Integrity_Check_{Ts()}.sql");
        }

        private async Task<Rule10RunReviewViewModel?> LoadAuthorizedSavedRunAsync(int runId, bool requireDownloadAccess)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule10.GetSavedRunAsync(runId, user?.Email, includeFullResults: requireDownloadAccess);
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

        private static bool CanDownloadSavedRun(Rule10RunReviewViewModel review, string systemRole)
            => ValidationRunAccessPolicy.CanDownloadSignedResults(systemRole, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff);

        private static bool CanViewSavedRun(Rule10RunReviewViewModel review, string systemRole)
            => ValidationRunAccessPolicy.CanViewSignedResults(systemRole, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff);

        private async Task<string> GetCurrentSystemRoleAsync(ApplicationUser? user)
        {
            var systemRole = await _systemDb.GetSystemRoleAsync(user);
            if (!string.IsNullOrWhiteSpace(systemRole))
                return systemRole!;

            var roles = user != null ? await _users.GetRolesAsync(user) : new List<string>();
            return roles.FirstOrDefault() ?? "";
        }

        private async Task<bool> CanEditWorkspaceAsync(int clientId, ApplicationUser? user, string role)
        {
            if (user == null || !string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) || clientId <= 0)
                return false;
            if (!await _systemDb.CanAccessClientResultsAsync(clientId, user, role))
                return false;
            var engagementRole = await _systemDb.GetEngagementRoleAsync(clientId, user, role);
            return ValidationRunAccessPolicy.IsAssignedDataAnalyst(engagementRole);
        }

        private static bool CanViewWorkspaceResults(string role, Rule10WorkspaceStateViewModel? workspace)
        {
            if (workspace == null)
                return false;

            return ValidationRunAccessPolicy.CanViewSignedResults(role, workspace.CurrentUserEngagementRole, workspace.HasDataAnalystSignoff);
        }

        private async Task<Rule10ValidationSummary> ResolveExportSummaryAsync(Rule10ValidationSummary summary)
        {
            var user = await _users.GetUserAsync(User);

            if (summary.SavedRunId is int savedRunId && savedRunId > 0)
            {
                var review = await _rule10.GetSavedRunAsync(savedRunId, user?.Email);
                if (review?.Summary != null)
                {
                    return await _rule10.GetExportSummaryAsync(new Rule10ValidationRequest
                    {
                        ClientId = review.ClientId,
                        RunId = review.RunId,
                        RuleNumber = review.RuleNumber,
                        Server = review.SourceServer,
                        Database = review.Summary.Database,
                        Driver = "ODBC Driver 17 for SQL Server",
                        QualTable = review.Summary.QualTable,
                        StudTable = review.Summary.StudTable,
                        CregTable = review.Summary.CregTable,
                        CrseTable = review.Summary.CrseTable,
                        QualColumn = review.Summary.QualColumn,
                        StudColumn = review.Summary.StudColumn,
                        CregColumn = review.Summary.CregColumn,
                        CrseColumn = review.Summary.CrseColumn,
                        RuleParameterJson = review.Summary.RuleParameterJson,
                        Rule10JoinConfigJson = review.Summary.Rule10JoinConfigJson
                    });
                }
            }

            if (summary.ClientId > 0)
            {
                var workspace = await _rule10.GetCurrentWorkspaceStateAsync(summary.RuleNumber, summary.ClientId, user?.Email, includeSummary: false);
                if (workspace?.RunId is int workspaceRunId && workspaceRunId > 0)
                {
                    var review = await _rule10.GetSavedRunAsync(workspaceRunId, user?.Email);
                    if (review?.Summary != null)
                    {
                        return await _rule10.GetExportSummaryAsync(new Rule10ValidationRequest
                        {
                            ClientId = review.ClientId,
                            RunId = review.RunId,
                            RuleNumber = review.RuleNumber,
                            Server = review.SourceServer,
                            Database = review.Summary.Database,
                            Driver = "ODBC Driver 17 for SQL Server",
                            QualTable = review.Summary.QualTable,
                            StudTable = review.Summary.StudTable,
                            CregTable = review.Summary.CregTable,
                            CrseTable = review.Summary.CrseTable,
                            QualColumn = review.Summary.QualColumn,
                            StudColumn = review.Summary.StudColumn,
                            CregColumn = review.Summary.CregColumn,
                            CrseColumn = review.Summary.CrseColumn,
                            RuleParameterJson = review.Summary.RuleParameterJson,
                            Rule10JoinConfigJson = review.Summary.Rule10JoinConfigJson
                        });
                    }
                }
            }

            return summary;
        }

        private async Task<object> RequireDataAnalystAsync<T>(Func<Task<T>> action) where T : class
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                return new { success = false, error = "Only the assigned data analyst can configure or run Rules 1-10." };
            }

            var result = await action();
            if (result == null)
                return new { success = false, error = "Rules 1-10 action returned no result." };

            return result;
        }

        private static string Ts() => DateTime.Now.ToString("yyyyMMdd_HHmmss");

        private int ResolveRuleNumber(int? requestRuleNumber = null)
        {
            if (requestRuleNumber.HasValue && IntegrityRuleCatalog.IsSupported(requestRuleNumber.Value))
                return requestRuleNumber.Value;

            if (RouteData.Values.TryGetValue("ruleNumber", out var routeValue) &&
                int.TryParse(Convert.ToString(routeValue), out var routeRuleNumber) &&
                IntegrityRuleCatalog.IsSupported(routeRuleNumber))
            {
                return routeRuleNumber;
            }

            if (Request.Query.TryGetValue("ruleNumber", out var queryValue) &&
                int.TryParse(queryValue, out var queryRuleNumber) &&
                IntegrityRuleCatalog.IsSupported(queryRuleNumber))
            {
                return queryRuleNumber;
            }

            return 10;
        }
    }
}
