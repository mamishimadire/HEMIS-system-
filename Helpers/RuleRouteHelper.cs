namespace HemisAudit.Helpers
{
    public static class RuleRouteHelper
    {
        public static string GetControllerName(int ruleNumber) => ruleNumber switch
        {
            29 => "Rule29",
            30 => "Rule30",
            31 => "Rule31",
            32 => "Rule32",
            34 => "Rule34",
            36 => "Rule36",
            _ => $"Rule{ruleNumber}"
        };
    }
}
