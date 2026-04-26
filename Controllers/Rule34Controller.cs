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
    public class Rule34Controller : Controller
    {
        private readonly IRule34Service _rule34;
        private readonly IExportService _export;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;

        public Rule34Controller(
            IRule34Service rule34,
            IExportService export,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb)
        {
            _rule34 = rule34;
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
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForWorkspace(34, clientId);
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetWorkspaceState(int clientId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();

            if (clientId <= 0)
            {
                return Json(new
                {
                    success = true,
                    hasWorkspace = false
                });
            }

            if (!await _systemDb.CanAccessClientResultsAsync(clientId, user, role))
            {
                Response.StatusCode = StatusCodes.Status403Forbidden;
                return Json(new
                {
                    success = false,
                    error = "You cannot access this engagement."
                });
            }

            var workspace = await _rule34.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);

            if (workspace != null)
                workspace.ResultsVisible = resultsVisible;

            if (workspace != null && !resultsVisible)
            {
                workspace.Server = "";
                workspace.Database = "";
                workspace.Driver = "ODBC Driver 17 for SQL Server";
                workspace.TableName = "";
                workspace.FirstDayColumn = "";
                workspace.LastDayColumn = "";
                workspace.CensusDateColumn = "";
                workspace.Summary = null;
            }

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
            var review = await _rule34.GetSavedRunAsync(id, user?.Email);
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
                34,
                review.ClientId,
                clientDetail?.ValidationRuns,
                role,
                review.CurrentUserEngagementRole);
            ViewBag.CanOpenWorkspace =
                !isArchived &&
                await _systemDb.CanAccessClientModuleAsync(review.ClientId, user, role) &&
                (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(review.CurrentUserEngagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            return View(review);
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule34.GetDatabasesAsync(model.Server, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule34.GetTablesAsync(model.Server, model.Database, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetColumns([FromBody] Rule34GetColumnsRequest model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule34.GetColumnsAsync(model.Server, model.Database, model.Driver, model.TableName)));

        [HttpPost]
        public async Task<IActionResult> VerifyTable([FromBody] Rule34VerifyRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule34.VerifyTableAsync(request)));

        [HttpPost]
        public async Task<IActionResult> LoadHolidays([FromBody] Rule34HolidayLoadRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule34.LoadHolidaysAsync(request.StartYear, request.EndYear)));

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] Rule34ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
            {
                return Json(new Rule34ValidationSummary
                {
                    Success = false,
                    Error = "Select an approved engagement before running validation."
                });
            }

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
            {
                return Json(new Rule34ValidationSummary
                {
                    Success = false,
                    Error = "You cannot access this engagement."
                });
            }

            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new Rule34ValidationSummary
                {
                    Success = false,
                    Error = "Only the assigned data analyst can run Rule 34."
                });
            }

            var result = await _rule34.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);

            if (result.Success)
            {
                await _audit.LogAsync(
                    "run_validation",
                    $"Rule 34 on client {request.ClientId}: {result.Status} ({result.FailCount} exceptions), run {result.SavedRunId}",
                    user?.Id,
                    user?.Email);
            }

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> BeginWorkspaceEdit([FromBody] Rule34ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (!await CanEditWorkspaceAsync(request.ClientId, user, role))
            {
                return Json(new Rule34WorkspaceSaveResult
                {
                    Success = false,
                    Error = "Only the assigned data analyst can edit a saved workspace."
                });
            }

            if (!request.RunId.HasValue || request.RunId.Value <= 0)
            {
                return Json(new Rule34WorkspaceSaveResult
                {
                    Success = false,
                    Error = "Select a saved run before editing the workspace."
                });
            }

            var result = await _rule34.BeginWorkspaceEditAsync(request.RunId.Value, user!.Email!, user.FullName);
            if (result.Success)
            {
                await _audit.LogAsync(
                    "workspace_edit_started",
                    $"DataAnalyst started editing Rule 34 run {request.RunId.Value}. Existing signoffs were cleared.",
                    user?.Id,
                    user?.Email);
            }

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] Rule34ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (!await CanEditWorkspaceAsync(request.ClientId, user, role))
            {
                return Json(new Rule34WorkspaceSaveResult
                {
                    Success = false,
                    Error = "Only the assigned data analyst can save a workspace."
                });
            }

            var result = await _rule34.SaveWorkspaceAsync(request, user!.Email!, user.FullName);
            if (result.Success)
            {
                await _audit.LogAsync(
                    "save_validation_workspace",
                    $"DataAnalyst saved Rule 34 workspace for client {request.ClientId}. Signoffs cleared: {result.ClearedSignoffCount ?? 0}",
                    user?.Id,
                    user?.Email);
            }

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SignOffWorkspace([FromBody] Rule34WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0)
            {
                return Json(new
                {
                    success = false,
                    error = "Select an engagement before signing off."
                });
            }

            if (!await CanSignWorkspaceAsync(model.ClientId, user, role))
            {
                return Json(new
                {
                    success = false,
                    error = "Only the assigned data analyst, manager, or director can sign off the workspace."
                });
            }

            if (!model.RunId.HasValue || model.RunId.Value <= 0)
            {
                return Json(new
                {
                    success = false,
                    error = "Run validation first so the workspace is saved."
                });
            }

            var review = await _rule34.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
            {
                return Json(new
                {
                    success = false,
                    error = "The saved validation run could not be found for this engagement."
                });
            }

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
            {
                return Json(new
                {
                    success = false,
                    error = "Archived engagements are read-only. Signoff is disabled."
                });
            }

            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) &&
                !review.HasDataAnalystSignoff)
            {
                return Json(new
                {
                    success = false,
                    error = "The assigned data analyst must sign off before this review can be completed."
                });
            }

            try
            {
                await _rule34.AddOrUpdateSignoffAsync(model.RunId.Value, user!.Email!, model.Comment);
                await _audit.LogAsync(
                    "signoff_validation_run",
                    $"Rule 34 signoff saved for run {model.RunId.Value} from module workspace",
                    user.Id,
                    user.Email);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    error = ex.Message
                });
            }

            var workspace = await _rule34.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email, includeSummary: false);
            return Json(new
            {
                success = true,
                message = "Signoff saved.",
                workspace
            });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveWorkspaceSignoff([FromBody] Rule34WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0 || !model.RunId.HasValue || model.RunId.Value <= 0)
            {
                return Json(new
                {
                    success = false,
                    error = "Select a saved run before removing signoff."
                });
            }

            if (!await CanSignWorkspaceAsync(model.ClientId, user, role))
            {
                return Json(new
                {
                    success = false,
                    error = "Only the assigned data analyst, manager, or director can remove their signoff."
                });
            }

            var review = await _rule34.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
            {
                return Json(new
                {
                    success = false,
                    error = "The saved validation run could not be found for this engagement."
                });
            }

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
            {
                return Json(new
                {
                    success = false,
                    error = "Archived engagements are read-only. Signoff removal is disabled."
                });
            }

            if (!review.CurrentUserHasSignedOff)
            {
                return Json(new
                {
                    success = false,
                    error = "You can only remove your own signoff."
                });
            }

            try
            {
                await _rule34.RemoveSignoffAsync(model.RunId.Value, user!.Email!);
                await _audit.LogAsync(
                    "remove_validation_signoff",
                    $"{review.CurrentUserEngagementRole} removed signoff for Rule 34 run {model.RunId.Value} from module workspace",
                    user.Id,
                    user.Email);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    error = ex.Message
                });
            }

            var workspace = await _rule34.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email, includeSummary: false);
            return Json(new
            {
                success = true,
                message = "Signoff removed.",
                workspace
            });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSql([FromBody] Rule34ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId > 0 && !await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
            {
                return Json(new Rule34SqlResult
                {
                    Success = false,
                    Error = "You cannot access this engagement."
                });
            }

            return Json(await RequireDataAnalystAsync(async () =>
                new Rule34SqlResult
                {
                    Success = true,
                    Sql = await _rule34.GenerateSqlAsync(request)
                }));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSignoff(Rule34RunSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule34.GetSavedRunAsync(model.RunId, user?.Email);
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

            if (!review.CanCurrentUserSignOff)
            {
                TempData["Error"] = "Only the assigned data analyst, manager, or director can sign off this run.";
                return RedirectToAction(nameof(Run), new { id = model.RunId });
            }

            if (!string.Equals(review.CurrentUserEngagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) &&
                !review.HasDataAnalystSignoff)
            {
                TempData["Error"] = "The assigned data analyst must sign off before this review can be completed.";
                return RedirectToAction(nameof(Run), new { id = model.RunId });
            }

            await _rule34.AddOrUpdateSignoffAsync(model.RunId, user!.Email!, model.Comment);
            await _audit.LogAsync(
                "signoff_validation_run",
                $"{review.CurrentUserEngagementRole} signed off Rule 34 run {model.RunId}",
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
            var review = await _rule34.GetSavedRunAsync(runId, user?.Email);
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

            if (!review.CurrentUserHasSignedOff)
            {
                TempData["Error"] = "You can only remove your own signoff.";
                return RedirectToAction(nameof(Run), new { id = runId });
            }

            await _rule34.RemoveSignoffAsync(runId, user!.Email!);
            await _audit.LogAsync(
                "remove_validation_signoff",
                $"{review.CurrentUserEngagementRole} removed signoff for Rule 34 run {runId}",
                user.Id,
                user.Email);

            TempData["Success"] = "Your signoff was removed.";
            return RedirectToAction(nameof(Run), new { id = runId });
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedExcel(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null)
                return RedirectToAction(nameof(Run), new { id = runId });

            var bytes = _export.ExportExcel(review.Summary);
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Rule34_Census_Date_Validation_Run_{runId}.xlsx");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedCsv(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null)
                return RedirectToAction(nameof(Run), new { id = runId });

            var bytes = _export.ExportCsv(review.Summary, false);
            return File(bytes, "text/csv", $"Rule34_Validation_Results_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedExceptionsCsv(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null)
                return RedirectToAction(nameof(Run), new { id = runId });

            var bytes = _export.ExportCsv(review.Summary, true);
            return File(bytes, "text/csv", $"Rule34_Census_Date_Exceptions_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedSql(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null)
                return RedirectToAction(nameof(Run), new { id = runId });

            var request = new Rule34ValidationRequest
            {
                ClientId = review.ClientId,
                Database = review.Summary.Database,
                TableName = review.Summary.TableName,
                FirstDayColumn = review.Summary.FirstDayColumn,
                LastDayColumn = review.Summary.LastDayColumn,
                CensusDateColumn = review.Summary.CensusDateColumn,
                StartYear = review.Summary.StartYear,
                EndYear = review.Summary.EndYear
            };

            var bytes = _export.ExportSql(await _rule34.GenerateSqlAsync(request));
            return File(bytes, "application/sql", $"Rule34_Census_Date_Validation_Run_{runId}.sql");
        }

        [HttpPost]
        public IActionResult DownloadExcel([FromBody] Rule34ValidationSummary summary)
        {
            var bytes = _export.ExportExcel(summary);
            SaveToDesktop($"Rule34_Census_Date_Validation_{Ts()}.xlsx", bytes);
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Rule34_Census_Date_Validation_{Ts()}.xlsx");
        }

        [HttpPost]
        public IActionResult DownloadCsv([FromBody] Rule34ValidationSummary summary)
        {
            var fileName = $"Rule34_Validation_Results_{Ts()}.csv";
            var bytes = _export.ExportCsv(summary, false);
            SaveToDesktop(fileName, bytes);
            return File(bytes, "text/csv", fileName);
        }

        [HttpPost]
        public IActionResult DownloadExceptionsCsv([FromBody] Rule34ValidationSummary summary)
        {
            var fileName = $"Rule34_Census_Date_Exceptions_{Ts()}.csv";
            var bytes = _export.ExportCsv(summary, true);
            SaveToDesktop(fileName, bytes);
            return File(bytes, "text/csv", fileName);
        }

        [HttpPost]
        public async Task<IActionResult> DownloadSql([FromBody] Rule34ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new
                {
                    success = false,
                    error = "Only the assigned data analyst can download the SQL script."
                });
            }

            var fileName = $"Rule34_Census_Date_Validation_{Ts()}.sql";
            var bytes = _export.ExportSql(await _rule34.GenerateSqlAsync(request));
            SaveToDesktop(fileName, bytes);
            return File(bytes, "application/sql", fileName);
        }

        private async Task<Rule34RunReviewViewModel?> LoadAuthorizedSavedRunAsync(int runId, bool requireDownloadAccess)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule34.GetSavedRunAsync(runId, user?.Email);
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

        private static bool CanDownloadSavedRun(Rule34RunReviewViewModel review, string systemRole)
        {
            if (string.Equals(systemRole, "Admin", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(review.CurrentUserEngagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return true;

            return review.CanCurrentUserDownload && review.HasDataAnalystSignoff;
        }

        private static bool CanViewSavedRun(Rule34RunReviewViewModel review, string systemRole)
        {
            if (string.Equals(systemRole, "Admin", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(review.CurrentUserEngagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return true;

            return review.HasDataAnalystSignoff;
        }

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
            if (user == null)
                return false;

            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return false;

            if (clientId <= 0)
                return false;

            if (!await _systemDb.CanAccessClientResultsAsync(clientId, user, role))
                return false;

            var engagementRole = await _systemDb.GetEngagementRoleAsync(clientId, user, role);
            return string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> CanSignWorkspaceAsync(int clientId, ApplicationUser? user, string role)
        {
            if (user == null)
                return false;

            if (clientId <= 0)
                return false;

            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!await _systemDb.CanAccessClientResultsAsync(clientId, user, role))
                return false;

            var engagementRole = await _systemDb.GetEngagementRoleAsync(clientId, user, role);
            return string.Equals(engagementRole, role, StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanViewWorkspaceResults(string role, Rule34WorkspaceStateViewModel? workspace)
        {
            if (workspace == null)
                return false;

            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return true;

            return workspace.HasDataAnalystSignoff;
        }

        private async Task<object> RequireDataAnalystAsync<T>(Func<Task<T>> action)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                return new
                {
                    success = false,
                    error = "Only the assigned data analyst can configure or run Rule 34."
                };
            }

            return await action();
        }

        private static string Ts() => DateTime.Now.ToString("yyyyMMdd_HHmmss");

        private static void SaveToDesktop(string fileName, byte[] bytes)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
                return;

            var path = Path.Combine(desktop, fileName);
            System.IO.File.WriteAllBytes(path, bytes);
        }
    }
}
