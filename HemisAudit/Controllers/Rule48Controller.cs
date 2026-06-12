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
    public class Rule48Controller : Controller
    {
        private readonly IRule48Service _rule48;
        private readonly IExportService _export;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;

        public Rule48Controller(
            IRule48Service rule48,
            IExportService export,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb)
        {
            _rule48   = rule48;
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
            ViewBag.ModuleNavigation  = ModuleSequenceNavigationHelper.BuildForWorkspace(48, clientId);
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

            var workspace      = await _rule48.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;

            if (workspace != null && !resultsVisible) workspace.Summary = null;

            return Json(new { success = true, hasWorkspace = workspace != null, resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule48.GetDatabasesAsync(model.Server, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule48.GetTablesAsync(model.Server, model.Database, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetColumns([FromBody] Rule41GetColumnsRequest model) =>
            Json(await RequireDataAnalystAsync(async () =>
                await _rule48.GetColumnsAsync(model.Server, model.Database, model.Driver, model.TableName)));

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] Rule41VerifyRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule48.VerifyTablesAsync(request)));

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
                return Json(new Rule41ValidationSummary { Success = false, Error = "Only the assigned data analyst can run Rule 48." });

            async Task<Rule41ValidationSummary> ExecuteAsync(IRule48Service svc, IAuditLogService auditSvc)
            {
                var result = await svc.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);
                if (result.Success)
                {
                    await auditSvc.LogAsync(
                        "run_validation",
                        $"Rule 48 on client {request.ClientId}: {result.Status} ({result.DisagreeCount + result.MissingCount} exceptions), run {result.SavedRunId}",
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
                    "Rule 48 validation",
                    async (sp, ct) => await ExecuteAsync(
                        sp.GetRequiredService<IRule48Service>(),
                        sp.GetRequiredService<IAuditLogService>()));
            }

            return Json(await ExecuteAsync(_rule48, _audit));
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

            var result = await _rule48.BeginWorkspaceEditAsync(request.RunId.Value, user!.Email!, user.FullName);
            if (result.Success)
                await _audit.LogAsync("workspace_edit_started", $"DataAnalyst started editing Rule 48 run {request.RunId.Value}.", user?.Id, user?.Email);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] Rule41ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (!await CanEditWorkspaceAsync(request.ClientId, user, role))
                return Json(new Rule41WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can save a workspace." });

            var result = await _rule48.SaveWorkspaceAsync(request, user!.Email!, user.FullName);
            if (result.Success)
                await _audit.LogAsync("save_validation_workspace", $"DataAnalyst saved Rule 48 workspace for client {request.ClientId}.", user?.Id, user?.Email);
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

            var review = await _rule48.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved run could not be found for this engagement." });

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
                return Json(new { success = false, error = "Archived engagements are read-only." });

            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
                return Json(new { success = false, error = "The assigned data analyst must sign off before this review can be completed." });

            try
            {
                await _rule48.AddOrUpdateSignoffAsync(model.RunId.Value, user!.Email!, model.Comment);
                await _audit.LogAsync("signoff_validation_run", $"Signed off Rule 48 run {model.RunId.Value}", user.Id, user.Email);
            }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }

            var workspace      = await _rule48.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
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

            var review = await _rule48.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved run could not be found." });

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
                return Json(new { success = false, error = "Archived engagements are read-only." });

            if (!review.CurrentUserHasSignedOff)
                return Json(new { success = false, error = "There is no signoff for your role to remove." });

            try { await _rule48.RemoveSignoffAsync(model.RunId.Value, user!.Email!); }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }

            var workspace      = await _rule48.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            await _audit.LogAsync("remove_validation_signoff", $"Removed signoff for Rule 48 run {model.RunId.Value}", user.Id, user.Email);
            return Json(new { success = true, message = "Signoff removed.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSql([FromBody] Rule41ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId > 0 && !await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule41SqlResult { Success = false, Error = "You cannot access this engagement." });

            return Json(RequireDataAnalystResult(() => new Rule41SqlResult { Success = true, Sql = _rule48.GenerateSql(request) }));
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
                Sql = Rule48RScriptGenerator.Generate(request) + RScriptScaffold.BuildAutoExportFooter("Rule48")
            }));
        }

        [HttpGet]
        public async Task<IActionResult> DownloadExcel([FromQuery] int runId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule48.GetSavedRunAsync(runId, user?.Email);
            if (review == null || !await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
                return NotFound();
            var bytes = BuildCsvBytes(review.Summary!, false);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Rule48_CRED_H16CRED_Agreement_Run_{runId}.xlsx");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadCsv([FromQuery] int runId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule48.GetSavedRunAsync(runId, user?.Email);
            if (review == null || !await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
                return NotFound();
            var bytes = BuildCsvBytes(review.Summary!, false);
            return File(bytes, "text/csv", $"Rule48_CRED_H16CRED_Agreement_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadExceptionsCsv([FromQuery] int runId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule48.GetSavedRunAsync(runId, user?.Email);
            if (review == null || !await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
                return NotFound();
            var bytes = BuildCsvBytes(review.Summary!, true);
            return File(bytes, "text/csv", $"Rule48_Exceptions_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSql([FromQuery] int runId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule48.GetSavedRunAsync(runId, user?.Email);
            if (review == null || !await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
                return NotFound();
            var req = new Rule41ValidationRequest
            {
                Database   = review.Summary!.Database,
                StudTable  = review.Summary.StudTable,
                AuditTable = review.Summary.AuditTable,
                StudKey    = review.Summary.StudKey,
                AuditKey   = review.Summary.AuditKey,
                Pairs      = review.Summary.Reconc?.Pairs ?? new()
            };
            var bytes = _export.ExportSql(_rule48.GenerateSql(req));
            return File(bytes, "application/sql", $"Rule48_CRED_H16CRED_Agreement_Run_{runId}.sql");
        }

        private static byte[] BuildCsvBytes(Rule41ValidationSummary summary, bool exceptionsOnly)
        {
            using var ms = new System.IO.MemoryStream();
            using var sw = new System.IO.StreamWriter(ms, System.Text.Encoding.UTF8);
            sw.WriteLine($"\"HEMIS RULE 48 Ã¢â‚¬â€œ {(exceptionsOnly ? "Exceptions" : "All Results")}\"");
            sw.WriteLine($"\"Database\",\"{summary.Database}\"");
            sw.WriteLine($"\"Timestamp\",\"{summary.Timestamp}\"");
            sw.WriteLine();
            var reconc = summary.Reconc;
            sw.WriteLine($"\"CRED vs H16CRED Reconciliation\"");
            sw.WriteLine($"\"CRED Table\",\"{reconc.StudTable}\"  \"H16CRED Table\",\"{reconc.AuditTable}\"");
            sw.WriteLine($"\"Total\",{reconc.TotalCount}  \"Agree\",{reconc.AgreeCount}  \"Disagree\",{reconc.DisagreeCount}  \"Missing\",{reconc.MissingCount}  \"Exception Rate\",{reconc.ExceptionRate:0.00}%");
            var labels = reconc.Pairs.Select(p => p.Label).ToList();
            sw.WriteLine("\"Row_No\",\"Cred_Ref\"," +
                string.Join(",", labels.Select(l => $"\"CRED_{l}\",\"H16CRED_{l}\",\"MATCH_{l}\"")) +
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
            sw.Flush();
            return ms.ToArray();
        }

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
                return new { success = false, error = "Only the assigned data analyst can configure or run Rule 48." };
            return await action();
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
