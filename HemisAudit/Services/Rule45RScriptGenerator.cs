using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule45RScriptGenerator
{
    public static string Generate(Rule41ValidationRequest req) =>
        Rule41RScriptGenerator.Generate(req, "45", "STU vs H16STU Agreement");
}
