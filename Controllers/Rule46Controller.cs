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
                Sql     = _rule46.GenerateControl1Sql(request) + "\n\n" + _rule46.GenerateControl2Sql(request)
            });
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
            var bytes = BuildCsvBytes(review.Summary!);
            return File(bytes, "text/csv", $"Rule46_Foundation_PQM_Run_{runId}.csv");
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
                Database = s.Database, StudTable = s.StudTable, StudKey = s.StudKey,
                StudFilterCol = s.StudFilterCol, StudFilterValue = s.StudFilterValue,
                QualTable = s.QualTable, QualKey = s.QualKey, QualNameCol = s.QualNameCol,
                CredTable = s.CredTable, CredStudKey = s.CredStudKey, CredCourseCol = s.CredCourseCol,
                PqmTable = s.PqmTable, PqmNameCol = s.PqmNameCol
            };
            var sqlText = _rule46.GenerateControl1Sql(req) + "\n\n" + _rule46.GenerateControl2Sql(req);
            var bytes = _export.ExportSql(sqlText);
            return File(bytes, "application/sql", $"Rule46_Foundation_PQM_Run_{runId}.sql");
        }

        private static byte[] BuildCsvBytes(Rule46ValidationSummary summary)
        {
            using var ms = new System.IO.MemoryStream();
            using var sw = new System.IO.StreamWriter(ms, System.Text.Encoding.UTF8);
            sw.WriteLine($"\"HEMIS RULE 46 â€“ Foundation Student PQM Validation\"");
            sw.WriteLine($"\"Database\",\"{summary.Database}\"");
            sw.WriteLine($"\"Timestamp\",\"{summary.Timestamp}\"");
            sw.WriteLine($"\"Total\",{summary.TotalValidated},\"Pass\",{summary.PassCount},\"Fail\",{summary.FailCount},\"Exception Rate\",{summary.ExceptionRate:0.00}%");
            sw.WriteLine();
            sw.WriteLine("\"Row\",\"Control\",\"Stud_ID\",\"Stud_Filter\",\"Qual_ID\",\"Qual_Name\",\"Cred_ID\",\"Cred_Course\",\"PQM_Name\",\"Result\",\"Detail\"");
            foreach (var row in summary.ValidationRows)
            {
                static string Q(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
                sw.WriteLine($"{row.RowNumber},{Q(row.ControlType)},{Q(row.StudId)},{Q(row.StudFilterValue)},{Q(row.QualId)},{Q(row.QualName)},{Q(row.CredId)},{Q(row.CredCourse)},{Q(row.PqmName)},{Q(row.ValidationResult)},{Q(row.ResultDetail)}");
            }
            sw.Flush();
            return ms.ToArray();
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
