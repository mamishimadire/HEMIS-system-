using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule16RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule16ValidationRequest req)
    {
        var studTable  = RString(req.StudTable);
        var cregTable  = RString(req.BridgeTable);
        var crseTable  = RString(req.CrseTable);

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

# Rule 16: Foundation CESM - Programme Type Validation
cesm_table <- '{studTable}'
qual_table <- '{studTable}'
stud_table <- '{cregTable}'
crse_table <- '{crseTable}'
pg_types   <- ''
governing  <- 'ALL'

cesm <- copy(ds[[cesm_table]]); safe_names(cesm)
stud <- copy(ds[[stud_table]]); safe_names(stud)
if (nchar(crse_table) > 0 && table_exists(crse_table)) {{
  crse <- copy(ds[[crse_table]]); safe_names(crse)
}} else {{
  crse <- data.table()
}}

force_char_trim(cesm, c('X001', 'X007', 'X019', 'X005'))
force_char_trim(stud, c('X007', 'X001'))
if (nrow(crse) > 0) force_char_trim(crse, c('X030', 'X091'))

cesm[, STUD_KEY := norm(col_val(.SD, 'X007'))]
exceptions <- cesm[is.na(STUD_KEY) | STUD_KEY == '',
                   .(StudentNo = STUD_KEY, Reason = 'Missing student reference')]
exceptions[, Status := 'FAIL']

print_summary(exceptions, 'Rule 16: Foundation CESM - Programme Type Validation')
cat(sprintf('STUD rows  : %d\n', nrow(stud)))
cat(sprintf('CESM rows  : %d\n', nrow(cesm)))
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }
}
