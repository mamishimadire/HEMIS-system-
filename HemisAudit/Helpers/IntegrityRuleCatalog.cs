namespace HemisAudit.Helpers
{
    public sealed record IntegrityRuleMetadata(
        int RuleNumber,
        string RuleLabel,
        string RuleTitle,
        string ShortDescription,
        string DetailedDescription,
        string CriteriaText,
        IReadOnlyList<string> RequiredTables);

    public static class IntegrityRuleCatalog
    {
        private static readonly IReadOnlyDictionary<int, IntegrityRuleMetadata> Rules =
            new Dictionary<int, IntegrityRuleMetadata>
            {
                [1] = new(1, "Rule 1", "Qualifications without qualification type",
                    "Check dbo_QUAL for blank qualification type values.",
                    "Verify whether any qualification in dbo_QUAL has a blank qualification type in _005.",
                    "ISBLANK(_005)",
                    ["QUAL"]),
                [2] = new(2, "Rule 2", "Qualifications without approval status",
                    "Check dbo_QUAL for blank approval status values.",
                    "Verify whether any qualification in dbo_QUAL has a blank approval status in _004.",
                    "ISBLANK(_004)",
                    ["QUAL"]),
                [3] = new(3, "Rule 3", "Duplicate qualification codes",
                    "Check dbo_QUAL for duplicate qualification codes.",
                    "Check for duplicate qualification codes on dbo_QUAL using the _001 field.",
                    "Checked for duplicates on dbo_QUAL using the _001 field.",
                    ["QUAL"]),
                [4] = new(4, "Rule 4", "Duplicate course codes",
                    "Check dbo_CRSE for duplicate course codes.",
                    "Check for duplicate course codes on dbo_CRSE using the _030 field.",
                    "Checked for duplicates on dbo_CRSE using the _030  field.",
                    ["CRSE"]),
                [5] = new(5, "Rule 5", "Invalid student numbers",
                    "Check dbo_STUD for placeholder student numbers.",
                    "The below filter was applied on dbo_STUD to check if are there any invalid student numbers.",
                    "NOT MAP(_007, \"9999999\")",
                    ["STUD"]),
                [6] = new(6, "Rule 6", "Students without foundation indicator",
                    "Check dbo_STUD for blank foundation indicator values.",
                    "The below filter was applied on dbo_STUD to check if are there any students with no foundation indicator.",
                    "ISBLANK(_106)",
                    ["STUD"]),
                [7] = new(7, "Rule 7", "Students with invalid qualifications",
                    "Check STUD to QUAL qualification linkage.",
                    "Verify whether any student is linked to a blank or invalid qualification between dbo_STUD and dbo_QUAL.",
                    "ISBLANK(dbo_STUD._001) OR dbo_STUD._001 <> dbo_QUAL._001",
                    ["STUD", "QUAL"]),
                [8] = new(8, "Rule 8", "Course registrations for invalid courses",
                    "Check CREG to CRSE course linkage.",
                    "Verify whether any course registration in dbo_CREG references a blank or invalid course in dbo_CRSE.",
                    "ISBLANK(dbo_CREG._030) OR dbo_CREG._030 <> dbo_CRSE._030",
                    ["CREG", "CRSE"]),
                [9] = new(9, "Rule 9", "Course registrations for ghost students",
                    "Check CREG to STUD student linkage.",
                    "The below filter was applied on dbo_STUD and dbo_CREG to check if there are any student course registrations for ghost students.",
                    "ISBLANK(dbo_CREG._007) OR dbo_CREG._007 <> dbo_STUD._007",
                    ["CREG", "STUD"]),
                [10] = new(10, "Rule 10", "Joining Rules",
                    "Review the joining rules and key fields used across the HEMIS datasets.",
                    "Review the table joins and key fields applied across dbo_CREG, dbo_CRSE, dbo_STUD, dbo_QUAL, dbo_CESM, dbo_CRED, census datasets, production datasets, employee data, dbo_PROF, and deceased student data.",
                    "The tables were joined on the documented key fields for each dataset.",
                    ["CREG", "CRSE", "STUD", "QUAL"])
            };

        public static IReadOnlyList<int> GetRuleNumbers() => Rules.Keys.OrderBy(x => x).ToList();

        public static IntegrityRuleMetadata Get(int ruleNumber)
        {
            if (Rules.TryGetValue(ruleNumber, out var metadata))
                return metadata;

            throw new ArgumentOutOfRangeException(nameof(ruleNumber), $"Integrity rule {ruleNumber} is not supported.");
        }

        public static bool IsSupported(int ruleNumber) => Rules.ContainsKey(ruleNumber);
    }
}
