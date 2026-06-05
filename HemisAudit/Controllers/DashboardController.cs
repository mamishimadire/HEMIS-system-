using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HemisAudit.Data;
using HemisAudit.Helpers;
using HemisAudit.Models;
using HemisAudit.ViewModels;
using HemisAudit.Services;

namespace HemisAudit.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext         _db;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService      _systemDb;

        public DashboardController(ApplicationDbContext db, UserManager<ApplicationUser> users, ISystemDatabaseService systemDb)
        {
            _db       = db;
            _users    = users;
            _systemDb = systemDb;
        }

        public async Task<IActionResult> Index(string scope = "active", string? q = null)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();
            var isAdmin = role == "Admin";
            var isDataAnalyst = role == "DataAnalyst";
            var normalizedScope = NormalizeScope(scope);
            var allAccessibleClients = await _systemDb.GetClientsAsync(user, role, scope: "all");
            var portfolioClients = await _systemDb.GetClientsAsync(user, role, search: q, scope: normalizedScope);
            var recentRuns = await _systemDb.GetRecentRunsAsync(user, role, 12);
            var currentRuns = await _systemDb.GetCurrentRunsAsync(user, role);

            if (!isAdmin && !isDataAnalyst)
            {
                recentRuns = recentRuns
                    .Where(run => run.HasDataAnalystSignoff)
                    .ToList();

                currentRuns = currentRuns
                    .Where(run => run.HasDataAnalystSignoff)
                    .ToList();
            }
            var signedCurrentRuns = currentRuns
                .Where(run => run.HasDataAnalystSignoff)
                .ToList();
            var currentRuleRuns = signedCurrentRuns
                .GroupBy(run => new { run.ClientId, run.RuleNumber })
                .Select(group => group
                    .OrderByDescending(run => run.RunAt)
                    .ThenByDescending(run => run.Id)
                    .First())
                .ToList();
            var activeClientsCount = await _systemDb.GetClientCountAsync(user, role, "active");
            var archivedClientsCount = await _systemDb.GetClientCountAsync(user, role, "archived");
            var favoriteClientsCount = await _systemDb.GetClientCountAsync(user, role, "favorites");
            var totalClientsCount = await _systemDb.GetClientCountAsync(user, role, "all");
            var pendingApprovalCount = allAccessibleClients.Count(c =>
                string.Equals(c.Status, "Pending", StringComparison.OrdinalIgnoreCase));
            var pendingApprovals = allAccessibleClients
                .Where(c => string.Equals(c.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.CreatedAt)
                .Take(8)
                .ToList();
            var reviewedAndCompletedRuns = currentRuleRuns.Count(run => run.IsReviewedAndCompleted);
            var unsignedAnalystRuns = 0;
            var awaitingReviewRuns = currentRuleRuns.Count(run => !run.IsReviewedAndCompleted);
            var needsReviewRuns = awaitingReviewRuns;
            var passedRuleRuns = currentRuleRuns.Count(IsPassedRun);
            var failedRuleRuns = currentRuleRuns.Count(IsFailedRun);
            var passedRuleRecords = currentRuleRuns.Sum(run => Math.Max(run.PassCount, 0));
            var failedRuleRecords = currentRuleRuns.Sum(run => Math.Max(run.FailCount, 0));
            var totalRuleRecords = currentRuleRuns.Sum(run => Math.Max(run.TotalValidated, 0));
            var passedRuleRecordRate = totalRuleRecords > 0
                ? Math.Round(passedRuleRecords * 100m / totalRuleRecords, 2)
                : 0m;
            var failedRuleRecordRate = totalRuleRecords > 0
                ? Math.Round(failedRuleRecords * 100m / totalRuleRecords, 2)
                : 0m;
            var analystSignedRuns = currentRuleRuns.Count;
            var managerSignedRuns = currentRuleRuns.Count(run => run.HasManagerSignoff);
            var directorSignedRuns = currentRuleRuns.Count(run => run.HasDirectorSignoff);
            var activeClientsForIndustry = allAccessibleClients
                .Where(c => c.IsActiveEngagement)
                .ToList();
            var industryBreakdown = activeClientsForIndustry
                .GroupBy(c => string.IsNullOrWhiteSpace(c.Industry) ? "Unspecified" : c.Industry)
                .Select(group => new DashboardIndustryMetric
                {
                    Industry = group.Key,
                    Count = group.Count()
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Industry)
                .Take(8)
                .ToList();
            var archiveReadyEngagements = allAccessibleClients.Count(client =>
                client.IsArchived ||
                (client.ValidationRunsCount > 0 &&
                 (
                     (client.LastRunStatus?.Contains("Completed", StringComparison.OrdinalIgnoreCase) ?? false) ||
                     (client.LatestSignedOffStatus?.Contains("Completed", StringComparison.OrdinalIgnoreCase) ?? false)
                 )));
            var ruleOutcomeBreakdown = RuleRouteHelper.GetSupportedRuleNumbers()
                .Select(ruleNumber =>
                {
                    var runsForRule = currentRuleRuns
                        .Where(run => run.RuleNumber == ruleNumber)
                        .ToList();

                    return new DashboardRuleOutcomeMetric
                    {
                        RuleNumber = ruleNumber,
                        RuleLabel = $"Rule {ruleNumber}",
                        PassedCount = runsForRule.Count(IsPassedRun),
                        FailedCount = runsForRule.Count(IsFailedRun)
                    };
                })
                .OrderBy(metric => metric.RuleNumber)
                .Where(metric => metric.PassedCount > 0 || metric.FailedCount > 0)
                .ToList();

            var vm = new DashboardViewModel
            {
                TotalClients = totalClientsCount,
                PendingApprovalClients = isAdmin ? pendingApprovalCount : 0,
                FavoriteClients = favoriteClientsCount,
                TotalUsers = isAdmin ? await _users.Users.CountAsync() : 0,
                TotalValidationRuns = await _systemDb.GetValidationRunCountAsync(user, role),
                TotalExceptions = await _systemDb.GetExceptionCountAsync(user, role),
                ApprovedClients = activeClientsCount,
                ArchivedClients = archivedClientsCount,
                DisplayedClientCount = portfolioClients.Count,
                ReviewedAndCompletedRuns = reviewedAndCompletedRuns,
                NeedsReviewRuns = needsReviewRuns,
                AwaitingReviewRuns = awaitingReviewRuns,
                PassedRuleRuns = passedRuleRuns,
                FailedRuleRuns = failedRuleRuns,
                PassedRuleRecords = passedRuleRecords,
                FailedRuleRecords = failedRuleRecords,
                PassedRuleRecordRate = passedRuleRecordRate,
                FailedRuleRecordRate = failedRuleRecordRate,
                AnalystSignedRuns = analystSignedRuns,
                UnsignedAnalystRuns = unsignedAnalystRuns,
                ManagerSignedRuns = managerSignedRuns,
                DirectorSignedRuns = directorSignedRuns,
                ArchiveReadyEngagements = archiveReadyEngagements,
                PortfolioClients = portfolioClients,
                ArchivedPortfolioClients = portfolioClients.Where(c => c.IsArchived).ToList(),
                PendingApprovalQueue = pendingApprovals,
                IndustryBreakdown = industryBreakdown,
                RuleOutcomeBreakdown = ruleOutcomeBreakdown,
                CurrentRuns = currentRuleRuns,
                RecentRuns = recentRuns,
                CurrentUserName = user?.FullName ?? "",
                CurrentUserRole = role,
                CurrentScope = normalizedScope,
                CurrentSearch = q
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleFavorite(int clientId, string scope = "active", string? q = null)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            try
            {
                await _systemDb.ToggleClientFavoriteAsync(clientId, user!, role);
                TempData["Success"] = "Favorite updated.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Index), new { scope, q });
        }

        private async Task<string> GetCurrentSystemRoleAsync(ApplicationUser? user)
        {
            var systemRole = await _systemDb.GetSystemRoleAsync(user);
            if (!string.IsNullOrWhiteSpace(systemRole))
                return systemRole!;

            var roles = user != null ? await _users.GetRolesAsync(user) : new List<string>();
            return roles.FirstOrDefault() ?? "";
        }

        private static string NormalizeScope(string? scope)
        {
            var value = (scope ?? "active").Trim().ToLowerInvariant();
            return value switch
            {
                "favorites" or "favourites" => "favorites",
                "archived" => "archived",
                "all" => "all",
                _ => "active"
            };
        }

        private static bool IsPassedRun(ValidationRunRow run)
        {
            if (string.Equals(run.Status, "PASS", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(run.Status, "FAIL", StringComparison.OrdinalIgnoreCase))
                return false;

            return run.TotalValidated > 0 && run.FailCount <= 0;
        }

        private static bool IsFailedRun(ValidationRunRow run)
        {
            if (string.Equals(run.Status, "FAIL", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(run.Status, "PASS", StringComparison.OrdinalIgnoreCase))
                return false;

            return run.FailCount > 0;
        }
    }
}
