using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace HemisAudit.Controllers;

[Authorize]
public class DirectivesController : Controller
{
    private const string BASE = "https://www.heda.co.za/Valpac_Help/";
    private readonly IHttpClientFactory _httpFactory;

    public DirectivesController(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Proxy([FromQuery] string url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith(BASE, StringComparison.OrdinalIgnoreCase))
            return Json(new { success = false, error = "Invalid URL" });

        try
        {
            var client = _httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.5");
            req.Headers.TryAddWithoutValidation("Referer", BASE);

            using var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return Json(new { success = false, error = $"Site returned {(int)resp.StatusCode}" });

            var html = await resp.Content.ReadAsStringAsync();

            // Remove <style> and <link rel="stylesheet"> blocks
            html = Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<link[^>]*rel=""stylesheet""[^>]*/?>", "", RegexOptions.IgnoreCase);

            // Rewrite <a href="..."> — only inside <a> tags
            html = Regex.Replace(html, @"<a\b([^>]*)>", m =>
            {
                var attrs = m.Groups[1].Value;
                attrs = Regex.Replace(attrs, @"href=""([^""]+)""", hm =>
                {
                    var href = hm.Groups[1].Value;
                    if (href.StartsWith("#") || href.StartsWith("mailto:") ||
                        href.StartsWith("javascript:") || href.StartsWith("tel:"))
                        return hm.Value;

                    string abs;
                    if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        abs = href;
                    else if (href.StartsWith("//"))
                        abs = "https:" + href;
                    else if (href.StartsWith("/"))
                        abs = "https://www.heda.co.za" + href;
                    else
                        abs = BASE + href;

                    if (abs.Contains("heda.co.za/Valpac_Help/", StringComparison.OrdinalIgnoreCase))
                    {
                        var escaped = abs.Replace("\"", "&quot;");
                        return $"href=\"javascript:void(0)\" data-valpac-url=\"{escaped}\"";
                    }
                    return hm.Value;
                }, RegexOptions.IgnoreCase);
                return $"<a{attrs}>";
            }, RegexOptions.IgnoreCase);

            // Fix relative <img src>
            html = Regex.Replace(html, @"<img\b([^>]*)src=""(?!https?:|//)([^""]+)""", m =>
            {
                var prefix = m.Groups[1].Value;
                var src = m.Groups[2].Value;
                var abs = src.StartsWith("/") ? "https://www.heda.co.za" + src : BASE + src;
                return $"<img{prefix}src=\"{abs}\"";
            }, RegexOptions.IgnoreCase);

            // Extract <body> content
            var bodyMatch = Regex.Match(html, @"<body[^>]*>([\s\S]*?)</body>", RegexOptions.IgnoreCase);
            var body = bodyMatch.Success ? bodyMatch.Groups[1].Value : html;
            body = body.Replace("&nbsp;", " ").Replace("�", "");

            var titleMatch = Regex.Match(html, @"<title[^>]*>([^<]*)</title>", RegexOptions.IgnoreCase);
            var title = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : "Valpac Help";

            return Json(new { success = true, title, body, url });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult Sitemap()
    {
        var pages = new[]
        {
            new { id = "main",     label = "Main Menu",                          url = BASE + "Main_menu.htm",              group = "Home",          sub = false },
            new { id = "intro",    label = "(A) Introduction",                   url = BASE + "Intro.htm",                  group = "Home",          sub = false },
            new { id = "history",  label = "(B) History of Amendments",          url = BASE + "History.htm",                group = "Home",          sub = false },
            new { id = "contacts", label = "(C) Contacts",                       url = BASE + "Contacts.htm",               group = "Home",          sub = false },
            new { id = "steps",    label = "(D) Steps in Preparing Returns",     url = BASE + "Steps.htm",                  group = "Preparation",   sub = false },
            new { id = "scopes",   label = "(E) File Scopes & Dates",            url = BASE + "Scopes.htm",                 group = "Preparation",   sub = false },
            new { id = "files",    label = "(F) File Structures",                url = BASE + "Files.htm",                  group = "Preparation",   sub = false },
            new { id = "ded_base", label = "(G) Base Element Dictionary",        url = BASE + "DEDBase.htm",                group = "Data Elements", sub = false },
            new { id = "e001",     label = "▸ 001–010 Qualification & Student",  url = BASE + "ded_001_010.htm",            group = "Data Elements", sub = true  },
            new { id = "e011",     label = "▸ 011–020 Student Demographics",     url = BASE + "DED_011_020.htm",            group = "Data Elements", sub = true  },
            new { id = "e021",     label = "▸ 021–030 Student Status",           url = BASE + "DED_021_030.htm",            group = "Data Elements", sub = true  },
            new { id = "e031",     label = "▸ 031–040 Course & Staff",           url = BASE + "DED_031_040.htm",            group = "Data Elements", sub = true  },
            new { id = "e041",     label = "▸ 041–050 Staff Employment",         url = BASE + "DED_041_050.htm",            group = "Data Elements", sub = true  },
            new { id = "e051",     label = "▸ 051–060 Student Info",             url = BASE + "DED_051_060.htm",            group = "Data Elements", sub = true  },
            new { id = "e061",     label = "▸ 061–070 Institution & Course",     url = BASE + "DED_061_070.htm",            group = "Data Elements", sub = true  },
            new { id = "e071",     label = "▸ 071–080 Addresses & Activity",     url = BASE + "DED_071_080.htm",            group = "Data Elements", sub = true  },
            new { id = "e081",     label = "▸ 081–090 Qualification Detail",     url = BASE + "Ded_081_090.htm",            group = "Data Elements", sub = true  },
            new { id = "e091",     label = "▸ 091–100 NQF & Post Doctoral",      url = BASE + "Ded_091_100.htm",            group = "Data Elements", sub = true  },
            new { id = "e101",     label = "▸ 101–106 Funding & Foundation",     url = BASE + "Ded_101_110.htm",            group = "Data Elements", sub = true  },
            new { id = "space201", label = "▸ 201–226 Building Space",           url = BASE + "DedSpace_201_210.htm",       group = "Data Elements", sub = true  },
            new { id = "ded_deriv",label = "(H) Derived Elements",               url = BASE + "DEDDeriv.htm",               group = "Data Elements", sub = false },
            new { id = "glossary", label = "(I) Glossary",                       url = BASE + "Glossary.htm",               group = "Reference",     sub = false },
            new { id = "credvals", label = "(J) Credit Values",                  url = BASE + "CredVals.htm",               group = "Reference",     sub = false },
            new { id = "edits",    label = "(K) Edit Validation Rules",          url = BASE + "Edits.htm",                  group = "Reference",     sub = false },
            new { id = "valpac",   label = "(L) Using Valpac.Net",               url = BASE + "Valpac.htm",                 group = "Reference",     sub = false },
            new { id = "cesm",     label = "(M) CESM Codes",                     url = BASE + "CESM.htm",                   group = "Reference",     sub = false },
            new { id = "circulars",label = "(Q) Circulars",                      url = BASE + "Circulars.htm",              group = "Reference",     sub = false },
            new { id = "audit08",  label = "(R) Audit Directives Feb 2008",      url = BASE + "Audit_directives_Feb08.htm", group = "Directives",    sub = false },
            new { id = "audit09",  label = "(S) Audit Directives Apr 2009",      url = BASE + "Audit_directives_Apr09.htm", group = "Directives",    sub = false }
        };
        return Json(pages);
    }
}
