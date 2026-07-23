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
    public class Rule40Controller : Controller
    {
        private readonly IRule40Service _rule40;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;

        public Rule40Controller(
            IRule40Service rule40,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb)
        {
            _rule40   = rule40;
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
            ViewBag.ModuleNavigation  = ModuleSequenceNavigationHelper.BuildForWorkspace(40, clientId);
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

            var workspace      = await _rule40.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
            var resultsVisible = CanViewResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            if (workspace != null && !resultsVisible) workspace.Summary = null;

            return Json(new { success = true, hasWorkspace = workspace != null, resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model)
        {
            var result = await RequireDataAnalystAsync(async () => await _rule40.GetDatabasesAsync(model.Server, model.Driver));
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model)
        {
            var result = await RequireDataAnalystAsync(async () => await _rule40.GetTablesAsync(model.Server, model.Database, model.Driver));
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] Rule40VerifyRequest request)
        {
            var result = await RequireDataAnalystAsync(async () => await _rule40.VerifyTablesAsync(request));
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] Rule40ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new Rule40ValidationSummary { Success = false, Error = "Select an approved engagement before running validation." });

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule40ValidationSummary { Success = false, Error = "You cannot access this engagement." });

            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return Json(new Rule40ValidationSummary { Success = false, Error = "Only the assigned data analyst can run Rule 40 validation." });

            var result = await _rule40.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);

            if (result.Success)
                await _audit.LogAsync("run_validation", $"Rule 40 on client {request.ClientId}: {result.Status} ({result.DisagreeCount + result.MissingInAsciiCount + result.MissingInValpacCount} exceptions).", user?.Id, user?.Email);

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] Rule40ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new { success = false, error = "Select an engagement before saving." });

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new { success = false, error = "You cannot access this engagement." });

            var result = await _rule40.SaveWorkspaceStateAsync(request.ClientId, request, user?.Email);

            if (result)
            {
                await _audit.LogAsync("save_validation_workspace", $"DataAnalyst saved Rule 40 workspace for client {request.ClientId}.", user?.Id, user?.Email);
                var workspace      = await _rule40.GetCurrentWorkspaceStateAsync(request.ClientId, user?.Email);
                var resultsVisible = CanViewResults(role, workspace);
                if (workspace != null) workspace.ResultsVisible = resultsVisible;
                if (workspace != null && !resultsVisible) workspace.Summary = null;
                return Json(new { success = true, message = "Workspace saved.", workspace, resultsVisible });
            }

            return Json(new { success = false, message = "Failed to save workspace." });
        }

        [HttpPost]
        public async Task<IActionResult> AddSignoff([FromBody] Rule40SignoffInput model)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return Json(new { success = false, error = "Not authenticated." });
            try
            {
                await _rule40.AddOrUpdateSignoffAsync(model.RunId!.Value, user.Email!, model.Comment);
                await _audit.LogAsync("add_signoff", $"Rule 40 signoff added for run {model.RunId}.", user.Id, user.Email);
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> RemoveSignoff([FromBody] Rule40SignoffInput model)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return Json(new { success = false, error = "Not authenticated." });
            try
            {
                await _rule40.RemoveSignoffAsync(model.RunId!.Value, user.Email!);
                await _audit.LogAsync("remove_signoff", $"Rule 40 signoff removed for run {model.RunId}.", user.Id, user.Email);
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSql([FromBody] Rule40ValidationRequest request)
        {
            var result = await RequireDataAnalystAsync(async () =>
            {
                var sql = _rule40.GenerateSql(request);
                return new { success = true, sql } as object;
            });
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> DownloadExcel([FromBody] Rule40SignoffInput model)
        {
            var summary = await _rule40.GetFullSummaryByRunIdAsync(model.RunId!.Value);
            if (summary == null) return NotFound();
            var bytes = BuildCsvBytes(summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Rule40_PROF_Agreement.xlsx");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadCsv([FromBody] Rule40SignoffInput model)
        {
            var summary = await _rule40.GetFullSummaryByRunIdAsync(model.RunId!.Value);
            if (summary == null) return NotFound();
            var bytes = BuildCsvBytes(summary);
            return File(bytes, "text/csv", "Rule40_PROF_Agreement.csv");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadSql([FromBody] Rule40ValidationRequest request)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(_rule40.GenerateSql(request));
            return File(bytes, "text/plain", "Rule40_PROF_Agreement.sql");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static readonly string[] CsvCols =
            ["_011", "_012", "_013", "_014", "_038", "_039", "_040", "_041", "_042", "_046", "_047", "_048"];

        private static readonly Dictionary<string, string> CsvLabels = new()
        {
            ["_011"]="Date of Birth", ["_012"]="Gender", ["_013"]="Race", ["_014"]="Nationality",
            ["_038"]="Empl. Commencement", ["_039"]="Personnel Category", ["_040"]="Rank",
            ["_041"]="Permanent/Temporary", ["_042"]="Full/Part-time",
            ["_046"]="Qualification Type", ["_047"]="Joint Appointment", ["_048"]="On Payroll Code"
        };

        private static byte[] BuildCsvBytes(Rule40ValidationSummary summary)
        {
            using var ms = new System.IO.MemoryStream();
            using var sw = new System.IO.StreamWriter(ms, System.Text.Encoding.UTF8);

            sw.WriteLine("HEMIS RULE 40 - PROF VALPAC vs ASCII Staff Agreement");
            sw.WriteLine($"\"VALPAC Table\",\"{summary.ValpacTable}\",\"ASCII Table\",\"{summary.AsciiTable}\"");
            sw.WriteLine($"\"Database\",\"{summary.Database}\",\"Timestamp\",\"{summary.Timestamp}\"");
            sw.WriteLine($"\"Total\",{summary.TotalCount},\"Agree\",{summary.AgreeCount},\"Disagree\",{summary.DisagreeCount},\"Missing in ASCII\",{summary.MissingInAsciiCount},\"Missing in VALPAC\",{summary.MissingInValpacCount},\"Exception Rate\",\"{summary.ExceptionRate:0.00}%\",\"Status\",\"{summary.Status}\"");
            sw.WriteLine();

            var header = "\"Staff Number (_037)\",\"Overall Result\",\"Disagree Detail\"," +
                         string.Join(",", CsvCols.SelectMany(c =>
                             new[] { $"\"VALPAC_{c} ({CsvLabels[c]})\"", $"\"ASCII_{c} ({CsvLabels[c]})\"", $"\"MATCH_{c}\"" }));
            sw.WriteLine(header);

            foreach (var row in summary.ReviewRows.Concat(summary.AgreeSample))
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"\"{row.StaffNumber}\",\"{row.OverallResult}\",\"{row.DisagreeDetail.Replace("\"", "\"\"")}\"");
                foreach (var col in CsvCols)
                {
                    if (row.Fields.TryGetValue(col, out var fv))
                        sb.Append($",\"{fv.ValpacValue}\",\"{fv.AsciiValue}\",\"{fv.Match}\"");
                    else
                        sb.Append(",\"—\",\"—\",\"—\"");
                }
                sw.WriteLine(sb);
            }

            sw.Flush();
            return ms.ToArray();
        }

        private static bool CanViewResults(string role, Rule40WorkspaceState? workspace)
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
