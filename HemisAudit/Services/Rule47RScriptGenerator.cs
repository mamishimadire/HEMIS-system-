using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule47RScriptGenerator
{
    public static string Generate(Rule41ValidationRequest req) =>
        Rule41RScriptGenerator.Generate(req, "47", "QUAL vs H16QUAL Agreement");
}
