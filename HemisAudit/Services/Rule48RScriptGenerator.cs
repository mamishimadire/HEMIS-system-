using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule48RScriptGenerator
{
    public static string Generate(Rule41ValidationRequest req) =>
        Rule41RScriptGenerator.Generate(req, "48", "CRED vs H16CRED Agreement");
}
