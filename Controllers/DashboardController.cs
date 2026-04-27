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
            var portfolioClients = await _systemDb.GetClientsAsync(user, role, search: q, scope: normalizedScope);
            var recentRuns = await _systemDb.GetRecentRunsAsync(user, role, 12);
            var currentRuns = await _systemDb.GetCurrentRunsAsync(user, role);
            var visibleClientIds = portfolioClients
                .Select(client => client.Id)
                .ToHashSet();

            recentRuns = recentRuns
                .Where(run => visibleClientIds.Contains(run.ClientId))
                .ToList();

            currentRuns = currentRuns
                .Where(run => visibleClientIds.Contains(run.ClientId))
                .ToList();

            if (!isAdmin && !isDataAnalyst)
            {
                recentRuns = recentRuns
                    .Where(run => ValidationRunAccessPolicy.CanViewSignedResults(role, role, run.HasDataAnalystSignoff))
                    .ToList();

                currentRuns = currentRuns
                    .Where(run => ValidationRunAccessPolicy.CanViewSignedResults(role, role, run.HasDataAnalystSignoff))
                    .ToList();
            }
            var latestRuleRuns = recentRuns
                .GroupBy(run => new { run.ClientId, run.RuleNumber })
                .Select(group => group
                    .OrderByDescending(run => run.IsCurrent)
                    .ThenByDescending(run => run.RunAt)
                    .First())
                .ToList();
            var activeClientsCount = await _systemDb.GetClientCountAsync(user, role, "active");
            var archivedClientsCount = await _systemDb.GetClientCountAsync(user, role, "archived");
            var favoriteClientsCount = await _systemDb.GetClientCountAsync(user, role, "favorites");
            var totalClientsCount = await _systemDb.GetClientCountAsync(user, role, "all");
            var pendingApprovalCount = portfolioClients.Count(c =>
                string.Equals(c.Status, "Pending", StringComparison.OrdinalIgnoreCase));
            var pendingApprovals = portfolioClients
                .Where(c => string.Equals(c.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .ToList();
            var reviewedAndCompletedRuns = currentRuns.Count(run => run.IsReviewedAndCompleted);
            var needsReviewRuns = currentRuns.Count(run => !run.IsReviewedAndCompleted);
            var passedRuleRuns = latestRuleRuns.Count(IsPassedRun);
            var failedRuleRuns = latestRuleRuns.Count(IsFailedRun);
            var passedRuleRecords = latestRuleRuns.Sum(run => Math.Max(run.PassCount, 0));
            var failedRuleRecords = latestRuleRuns.Sum(run => Math.Max(run.FailCount, 0));
            var totalRuleRecords = latestRuleRuns.Sum(run => Math.Max(run.TotalValidated, 0));
            var passedRuleRecordRate = totalRuleRecords > 0
                ? Math.Round(passedRuleRecords * 100m / totalRuleRecords, 2)
                : 0m;
            var failedRuleRecordRate = totalRuleRecords > 0
                ? Math.Round(failedRuleRecords * 100m / totalRuleRecords, 2)
                : 0m;
            var analystSignedRuns = recentRuns.Count(run => run.HasDataAnalystSignoff);
            var managerSignedRuns = recentRuns.Count(run => run.HasManagerSignoff);
            var directorSignedRuns = recentRuns.Count(run => run.HasDirectorSignoff);
            var activeClientsForIndustry = portfolioClients
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
            var ruleOutcomeBreakdown = latestRuleRuns
                .GroupBy(run => run.RuleNumber)
                .Select(group => new DashboardRuleOutcomeMetric
                {
                    RuleNumber = group.Key,
                    RuleLabel = $"Rule {group.Key}",
                    PassedCount = group.Count(IsPassedRun),
                    FailedCount = group.Count(IsFailedRun)
                })
                .OrderBy(metric => metric.RuleNumber)
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
                PassedRuleRuns = passedRuleRuns,
                FailedRuleRuns = failedRuleRuns,
                PassedRuleRecords = passedRuleRecords,
                FailedRuleRecords = failedRuleRecords,
                PassedRuleRecordRate = passedRuleRecordRate,
                FailedRuleRecordRate = failedRuleRecordRate,
                AnalystSignedRuns = analystSignedRuns,
                ManagerSignedRuns = managerSignedRuns,
                DirectorSignedRuns = directorSignedRuns,
                PortfolioClients = portfolioClients,
                ArchivedPortfolioClients = portfolioClients.Where(c => c.IsArchived).ToList(),
                PendingApprovalQueue = pendingApprovals,
                IndustryBreakdown = industryBreakdown,
                RuleOutcomeBreakdown = ruleOutcomeBreakdown,
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
