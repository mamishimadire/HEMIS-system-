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
    [Route("Rule40.1/[action]")]
    public class Rule4001Controller : Controller
    {
        private readonly IRule4001Service _rule4001;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;

        public Rule4001Controller(
            IRule4001Service rule4001,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb)
        {
            _rule4001 = rule4001;
            _audit    = audit;
            _users    = users;
            _systemDb = systemDb;
        }

        [Route("~/Rule40.1")]
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

            if (clientId > 0 && !await _systemDb.CanAccessClientResultsAsync(clientId, user, role))
            {
                TempData["Error"] = "You cannot access this engagement.";
                return RedirectToAction("Index", "Dashboard");
            }

            var clients = await _systemDb.GetClientsAsync(user, role, approvedOnly: true);
            ViewBag.Clients = clients.Select(c => new Client
            {
                Id = c.Id, Name = c.EngagementName, FiscalYear = c.MaconomyNumber,
                Status = c.Status, CreatedAt = c.CreatedAt, CreatedByUserId = "", IsActive = true
            }).ToList();
            ViewBag.ClientId          = clientId;
            ViewBag.CurrentSystemRole = role;
            ViewBag.ModuleNavigation  = ModuleSequenceNavigationHelper.BuildForWorkspace(4001, clientId);
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

            var workspace      = await _rule4001.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            if (workspace != null && !resultsVisible) workspace.Summary = null;

            return Json(new { success = true, hasWorkspace = workspace != null, resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model)
        {
            var result = await RequireDataAnalystAsync(async () => await _rule4001.GetDatabasesAsync(model.Server, model.Driver));
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model)
        {
            var result = await RequireDataAnalystAsync(async () => await _rule4001.GetTablesAsync(model.Server, model.Database, model.Driver));
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> GetTableColumns([FromBody] Rule40GetColumnsRequest request)
        {
            var result = await RequireDataAnalystAsync(async () =>
            {
                var columns = await _rule4001.GetTableColumnsAsync(request.Server, request.Database, request.Driver, request.TableName);
                return new { success = true, columns } as object;
            });
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] Rule4001VerifyRequest request)
        {
            var result = await RequireDataAnalystAsync(async () => await _rule4001.VerifyTablesAsync(request));
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] Rule4001ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new Rule4001ValidationSummary { Success = false, Error = "Select an approved engagement before running validation." });

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule4001ValidationSummary { Success = false, Error = "You cannot access this engagement." });

            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return Json(new Rule4001ValidationSummary { Success = false, Error = "Only the assigned data analyst can run Rule 40.1 validation." });

            var result = await _rule4001.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);

            if (result.Success)
                await _audit.LogAsync("run_validation", $"Rule 40.1 on client {request.ClientId}: {result.Status} ({result.MissingInSfteCount + result.MissingInValpacCount} exceptions).", user?.Id, user?.Email);

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] Rule4001ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new { success = false, error = "Select an engagement before saving." });

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new { success = false, error = "You cannot access this engagement." });

            var result = await _rule4001.SaveWorkspaceStateAsync(request.ClientId, request, user?.Email);

            if (result)
            {
                await _audit.LogAsync("save_validation_workspace", $"DataAnalyst saved Rule 40.1 workspace for client {request.ClientId}.", user?.Id, user?.Email);
                var workspace      = await _rule4001.GetCurrentWorkspaceStateAsync(request.ClientId, user?.Email);
                var resultsVisible = CanViewResults(role, workspace);
                if (workspace != null) workspace.ResultsVisible = resultsVisible;
                if (workspace != null && !resultsVisible) workspace.Summary = null;
                return Json(new { success = true, message = "Workspace saved.", workspace, resultsVisible });
            }

            return Json(new { success = false, message = "Failed to save workspace." });
        }

        [HttpPost]
        public async Task<IActionResult> SignOffWorkspace([FromBody] Rule4001SignoffInput model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0)
                return Json(new { success = false, error = "Select an engagement before signing off." });

            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can sign off." });

            if (!model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Run validation first." });

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
                return Json(new { success = false, error = "Archived engagements are read-only." });

            try
            {
                await _rule4001.AddOrUpdateSignoffAsync(model.RunId.Value, user!.Email!, model.Comment);
                await _audit.LogAsync("signoff_validation_run", $"Signed off Rule 40.1 run {model.RunId.Value}", user.Id, user.Email);
            }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }

            var workspace      = await _rule4001.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            if (workspace != null && !resultsVisible) workspace.Summary = null;
            return Json(new { success = true, message = "Signoff saved.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveWorkspaceSignoff([FromBody] Rule4001SignoffInput model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0 || !model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Select a saved run before removing signoff." });

            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can remove signoff." });

            try { await _rule4001.RemoveSignoffAsync(model.RunId.Value, user!.Email!); }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }

            var workspace      = await _rule4001.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            if (workspace != null && !resultsVisible) workspace.Summary = null;
            await _audit.LogAsync("remove_validation_signoff", $"Removed signoff for Rule 40.1 run {model.RunId.Value}", user!.Id, user.Email);
            return Json(new { success = true, message = "Signoff removed.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSql([FromBody] Rule4001ValidationRequest request)
        {
            var result = await RequireDataAnalystAsync(async () =>
            {
                var sql = _rule4001.GenerateSql(request);
                return new { success = true, sql } as object;
            });
            return Json(result);
        }

        [HttpGet]
        public async Task<IActionResult> DownloadExcel([FromQuery] int runId)
        {
            var user    = await _users.GetUserAsync(User);
            var role    = await GetCurrentSystemRoleAsync(user);
            var summary = await _rule4001.GetFullSummaryByRunIdAsync(runId);
            if (summary == null || !await _systemDb.CanAccessClientResultsAsync(summary.ClientId, user, role))
                return NotFound();
            var bytes = BuildCsvBytes(summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Rule40_1_StaffPresence_Run_{runId}.xlsx");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadCsv([FromQuery] int runId)
        {
            var user    = await _users.GetUserAsync(User);
            var role    = await GetCurrentSystemRoleAsync(user);
            var summary = await _rule4001.GetFullSummaryByRunIdAsync(runId);
            if (summary == null || !await _systemDb.CanAccessClientResultsAsync(summary.ClientId, user, role))
                return NotFound();
            var bytes = BuildCsvBytes(summary);
            return File(bytes, "text/csv", $"Rule40_1_StaffPresence_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSql([FromQuery] int runId)
        {
            var user    = await _users.GetUserAsync(User);
            var role    = await GetCurrentSystemRoleAsync(user);
            var summary = await _rule4001.GetFullSummaryByRunIdAsync(runId);
            if (summary == null || !await _systemDb.CanAccessClientResultsAsync(summary.ClientId, user, role))
                return NotFound();
            var req   = new Rule4001ValidationRequest { ValpacTable = summary.ValpacTable, SfteTable = summary.SfteTable, Database = summary.Database, Server = summary.Server };
            var bytes = System.Text.Encoding.UTF8.GetBytes(_rule4001.GenerateSql(req));
            return File(bytes, "application/sql", $"Rule40_1_StaffPresence_Run_{runId}.sql");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static byte[] BuildCsvBytes(Rule4001ValidationSummary summary)
        {
            using var ms = new System.IO.MemoryStream();
            using var sw = new System.IO.StreamWriter(ms, System.Text.Encoding.UTF8);

            sw.WriteLine("HEMIS RULE 40.1 - Staff VALPAC vs H16SFTE Staff Presence Check");
            sw.WriteLine($"\"VALPAC Table\",\"{summary.ValpacTable}\",\"SFTE Table\",\"{summary.SfteTable}\"");
            sw.WriteLine($"\"Database\",\"{summary.Database}\",\"Timestamp\",\"{summary.Timestamp}\"");
            sw.WriteLine($"\"Total\",{summary.TotalCount},\"Agree\",{summary.AgreeCount},\"Missing in H16SFTE\",{summary.MissingInSfteCount},\"Missing in VALPAC\",{summary.MissingInValpacCount},\"Exception Rate\",\"{summary.ExceptionRate:0.00}%\",\"Status\",\"{summary.Status}\"");
            sw.WriteLine();
            sw.WriteLine("\"Staff Number (_037)\",\"Result\"");

            foreach (var row in summary.ReviewRows.Concat(summary.AgreeSample))
                sw.WriteLine($"\"{row.StaffNumber}\",\"{row.OverallResult}\"");

            sw.Flush();
            return ms.ToArray();
        }

        private static bool CanViewResults(string role, Rule4001WorkspaceState? workspace)
        {
            if (workspace == null) return false;
            if (string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "Admin",        StringComparison.OrdinalIgnoreCase)) return true;
            return workspace.IsWorkspaceSaved && workspace.HasDataAnalystSignoff;
        }

        private async Task<object> RequireDataAnalystAsync<T>(Func<Task<T>> operation) where T : class
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return new { success = false, error = "Only data analysts can perform this operation." };
            return await operation();
        }

        private async Task<string> GetCurrentSystemRoleAsync(ApplicationUser? user)
        {
            var systemRole = await _systemDb.GetSystemRoleAsync(user);
            if (!string.IsNullOrWhiteSpace(systemRole)) return systemRole!;
            var roles = user != null ? await _users.GetRolesAsync(user) : new List<string>();
            return roles.FirstOrDefault() ?? "";
        }
    }
}
