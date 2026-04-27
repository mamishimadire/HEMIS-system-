using HemisAudit.ViewModels;

namespace HemisAudit.Helpers
{
    public static class ModuleSequenceNavigationHelper
    {
        private static readonly ModuleSequenceItem[] Modules =
        {
            new(23, "Rule 23", "Reconcile Datasets"),
            new(24, "Rule 24", "Reconcile Qualification Datasets"),
            new(25, "Rule 25", "Reconcile Course Datasets"),
            new(26, "Rule 26", "Bi-Directional 5-Control Validation"),
            new(27, "Rule 27", "Error Validation"),
            new(29, "Rule 29", "Single Column Filter"),
            new(30, "Rule 30", "Fatal Errors with Exclusions (PROF)"),
            new(31, "Rule 31", "Fatal Errors with Exclusions (QUAL)"),
            new(32, "Rule 32", "Fatal Errors with Exclusions"),
            new(34, "Rule 34", "Census Date Validation"),
            new(36, "Rule 36", "Deceased Students Validation")
        };

        public static ModuleSequenceNavigationViewModel? BuildForWorkspace(int currentRuleNumber, int clientId)
        {
            if (clientId <= 0)
                return null;

            var currentIndex = FindModuleIndex(currentRuleNumber);
            if (currentIndex < 0)
                return null;

            return new ModuleSequenceNavigationViewModel
            {
                CurrentRuleNumber = Modules[currentIndex].RuleNumber,
                CurrentRuleLabel = Modules[currentIndex].RuleLabel,
                CurrentRuleTitle = Modules[currentIndex].RuleTitle,
                BackToValidationRunsUrl = $"/Admin/ClientDetail/{clientId}#validation-runs-panel",
                BackToEngagementUrl = $"/Admin/ClientDetail/{clientId}",
                Previous = currentIndex > 0 ? BuildWorkspaceLink(Modules[currentIndex - 1], clientId) : null,
                Next = currentIndex < Modules.Length - 1 ? BuildWorkspaceLink(Modules[currentIndex + 1], clientId) : null
            };
        }

        public static ModuleSequenceNavigationViewModel? BuildForSavedRun(
            int currentRuleNumber,
            int clientId,
            IEnumerable<ValidationRunRow>? validationRuns,
            string systemRole,
            string currentEngagementRole)
        {
            if (clientId <= 0)
                return null;

            var currentIndex = FindModuleIndex(currentRuleNumber);
            if (currentIndex < 0)
                return null;

            var runs = validationRuns?.ToList() ?? new List<ValidationRunRow>();

            return new ModuleSequenceNavigationViewModel
            {
                CurrentRuleNumber = Modules[currentIndex].RuleNumber,
                CurrentRuleLabel = Modules[currentIndex].RuleLabel,
                CurrentRuleTitle = Modules[currentIndex].RuleTitle,
                Previous = currentIndex > 0
                    ? BuildSavedRunLink(Modules[currentIndex - 1], clientId, runs, systemRole, currentEngagementRole)
                    : null,
                Next = currentIndex < Modules.Length - 1
                    ? BuildSavedRunLink(Modules[currentIndex + 1], clientId, runs, systemRole, currentEngagementRole)
                    : null
            };
        }

        private static int FindModuleIndex(int currentRuleNumber) =>
            Array.FindIndex(Modules, item => item.RuleNumber == currentRuleNumber);

        private static ModuleSequenceLinkViewModel BuildWorkspaceLink(ModuleSequenceItem module, int clientId) =>
            new()
            {
                RuleNumber = module.RuleNumber,
                RuleLabel = module.RuleLabel,
                RuleTitle = module.RuleTitle,
                Url = $"/Rule{module.RuleNumber}?clientId={clientId}",
                OpensSavedRun = false
            };

        private static ModuleSequenceLinkViewModel BuildSavedRunLink(
            ModuleSequenceItem module,
            int clientId,
            List<ValidationRunRow> validationRuns,
            string systemRole,
            string currentEngagementRole)
        {
            var canOpenAnyRun =
                string.Equals(systemRole, "Admin", StringComparison.OrdinalIgnoreCase) ||
                ValidationRunAccessPolicy.CanAssignedUserDownload(currentEngagementRole);

            var candidateRun = validationRuns
                .Where(run => run.RuleNumber == module.RuleNumber)
                .Where(run => canOpenAnyRun || run.HasDataAnalystSignoff)
                .OrderByDescending(run => run.IsCurrent)
                .ThenByDescending(run => run.HasDataAnalystSignoff)
                .ThenByDescending(run => run.RunAt)
                .ThenByDescending(run => run.Id)
                .FirstOrDefault();

            return new ModuleSequenceLinkViewModel
            {
                RuleNumber = module.RuleNumber,
                RuleLabel = module.RuleLabel,
                RuleTitle = module.RuleTitle,
                Url = candidateRun != null
                    ? $"/Rule{module.RuleNumber}/Run/{candidateRun.Id}"
                    : $"/Rule{module.RuleNumber}?clientId={clientId}",
                OpensSavedRun = candidateRun != null
            };
        }

        private sealed record ModuleSequenceItem(int RuleNumber, string RuleLabel, string RuleTitle);
    }
}
