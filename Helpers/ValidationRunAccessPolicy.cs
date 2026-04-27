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

        public static bool CanViewSignedResults(string? systemRole, string? engagementRole, bool hasDataAnalystSignoff)
        {
            if (IsAdmin(systemRole))
                return true;

            return CanAssignedUserDownload(engagementRole);
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
    }
}
