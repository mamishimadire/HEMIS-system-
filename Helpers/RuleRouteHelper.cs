namespace HemisAudit.Helpers
{
    public static class RuleRouteHelper
    {
        public static string GetControllerName(int ruleNumber) => ruleNumber switch
        {
            15 => "Rule15",
            16 => "Rule16",
            17 => "Rule17",
            18 => "Rule18",
            19 => "Rule19",
            20 => "Rule20",
            22 => "Rule22",
            23 => "Rule23",
            24 => "Rule24",
            25 => "Rule25",
            26 => "Rule26",
            27 => "Rule27",
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
