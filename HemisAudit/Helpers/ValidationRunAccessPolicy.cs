using HemisAudit.Models;
using HemisAudit.Services;

namespace HemisAudit.Helpers
{
    public static class ValidationRunAccessPolicy
    {
        public static bool IsAdmin(string? systemRole) =>
            string.Equals(systemRole, "Admin", StringComparison.OrdinalIgnoreCase);

        public static bool IsAssignedDataAnalyst(string? engagementRole) =>
            string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase);

        public static bool CanAssignedUserSignOff(string? engagementRole) =>
            string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(engagementRole, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(engagementRole, "Director", StringComparison.OrdinalIgnoreCase);

        public static bool CanAssignedUserDownload(string? engagementRole) =>
            CanAssignedUserSignOff(engagementRole) ||
            string.Equals(engagementRole, "Trainee", StringComparison.OrdinalIgnoreCase);

        public static bool CanAssignedUserRemoveSignoff(string? engagementRole) =>
            CanAssignedUserSignOff(engagementRole);

        public static bool IsSignoffOwnedByEngagementRole(string? signoffRole, string? engagementRole) =>
            CanAssignedUserRemoveSignoff(engagementRole) &&
            string.Equals(signoffRole, engagementRole, StringComparison.OrdinalIgnoreCase);

        public static bool CanViewSignedResults(string? systemRole, string? engagementRole, bool hasDataAnalystSignoff)
        {
            if (IsAdmin(systemRole))
                return true;

            if (IsAssignedDataAnalyst(engagementRole))
                return true;

            return hasDataAnalystSignoff && CanAssignedUserDownload(engagementRole);
        }

        public static bool CanDownloadSignedResults(string? systemRole, string? engagementRole, bool hasDataAnalystSignoff)
        {
            if (IsAdmin(systemRole) || IsAssignedDataAnalyst(engagementRole))
                return true;

            return hasDataAnalystSignoff && CanAssignedUserDownload(engagementRole);
        }

        public static bool CanCompleteReviewSignoff(string? systemRole, string? engagementRole, bool hasDataAnalystSignoff)
        {
            if (IsAdmin(systemRole))
                return true;

            if (!CanAssignedUserSignOff(engagementRole))
                return false;

            return IsAssignedDataAnalyst(engagementRole) || hasDataAnalystSignoff;
        }

        public static async Task<bool> CanAssignedUserRemoveOwnSignoffAsync(
            ISystemDatabaseService systemDb,
            int clientId,
            ApplicationUser? user,
            string? systemRole)
        {
            if (systemDb == null || user == null || clientId <= 0)
                return false;

            var resolvedSystemRole = systemRole ?? string.Empty;
            if (!await systemDb.CanAccessClientResultsAsync(clientId, user, resolvedSystemRole))
                return false;

            if (IsAdmin(resolvedSystemRole))
                return true;

            var engagementRole = await systemDb.GetEngagementRoleAsync(clientId, user, resolvedSystemRole);
            return CanAssignedUserRemoveSignoff(engagementRole);
        }
    }
}
