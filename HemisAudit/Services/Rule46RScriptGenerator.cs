using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule46RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule46ValidationRequest req)
    {
        var studTable   = RString(req.StudTable);
        var qualTable   = RString(req.QualTable);
        var studIdCol   = RString(req.StudIdCol);
        var stud007Col  = RString(req.Stud007Col);

        return RScriptScaffold.BuildDataLoadingPrelude() + $@"

norm <- function(x) toupper(trimws(as.character(x)))

safe_names <- function(dt) {{
  setnames(dt, old = names(dt), new = gsub('^_', 'X', names(dt)))
  invisible(dt)
}}

force_char_trim <- function(dt, cols) {{
  for (col in cols) if (col %in% names(dt))
    set(dt, j = col, value = trimws(as.character(dt[[col]])))
  invisible(dt)
}}

col_val <- function(dt, col, default = NA_character_) {{
  if (!is.null(col) && nchar(col) > 0 && col %in% names(dt)) dt[[col]] else rep(default, nrow(dt))
}}

print_summary <- function(dt, rule_label) {{
  cat(sprintf('\n-- %s --\n', rule_label))
  cat(sprintf('Total rows: %d\n', nrow(dt)))
  if ('Status' %in% names(dt)) {{
    tbl <- dt[, .N, by = Status]
    for (i in seq_len(nrow(tbl))) cat(sprintf('  %-16s: %d\n', tbl$Status[i], tbl$N[i]))
  }}
}}

stud_table   <- '{studTable}'
qual_table   <- '{qualTable}'
stud_id_col  <- '{studIdCol}'
stud_007_col <- '{stud007Col}'

stud <- copy(ds[[stud_table]]); safe_names(stud)
qual <- copy(ds[[qual_table]]); safe_names(qual)

sc  <- gsub('^_', 'X', stud_007_col)
ic  <- gsub('^_', 'X', stud_id_col)
force_char_trim(stud, c(sc, ic, 'X001'))
force_char_trim(qual, c('X001', 'X091'))

fdn_qual <- qual[norm(col_val(.SD, 'X091')) == '1', .(QUAL_CODE = norm(X001))]
setkey(fdn_qual, QUAL_CODE)

stud[, QUAL_KEY := norm(X001)]
stud[, Status := fifelse(QUAL_KEY %in% fdn_qual$QUAL_CODE, 'PASS', 'FAIL - Not a foundation qualification')]

exceptions <- stud[Status != 'PASS']
print_summary(stud, 'Rule 46: Foundation Qualification')
cat(sprintf('Exceptions: %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions[, .(col_val(.SD, sc), QUAL_KEY, Status)])
";
    }
}
