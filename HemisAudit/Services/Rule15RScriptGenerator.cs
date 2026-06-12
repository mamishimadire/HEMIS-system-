using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule15RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule15ValidationRequest req)
    {
        var cesmTable  = RString(req.StudTable);
        var qualTable  = RString(req.StudTable);
        var studTable  = RString(req.BridgeTable);
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

# Rule 15: Foundation CESM - Course Registration Check
cesm_table <- '{cesmTable}'
qual_table <- '{qualTable}'
stud_table <- '{studTable}'
crse_table <- '{crseTable}'
pg_types   <- ''
governing  <- 'ALL'

cesm <- copy(ds[[cesm_table]]); safe_names(cesm)
qual <- copy(ds[[qual_table]]); safe_names(qual)
stud <- copy(ds[[stud_table]]); safe_names(stud)
if (nchar(crse_table) > 0 && table_exists(crse_table)) {{
  crse <- copy(ds[[crse_table]]); safe_names(crse)
}} else {{
  crse <- data.table()
}}

force_char_trim(cesm, c('X001', 'X007', 'X019', 'X005'))
force_char_trim(qual, c('X001', 'X003', 'X005'))
force_char_trim(stud, c('X007', 'X001'))
if (nrow(crse) > 0) force_char_trim(crse, c('X030', 'X091'))

valid_qual <- qual[!is.na(X001) & X001 != '', .(KEY = norm(X001), QualDesc = X003, QualType = X005)]
setkey(valid_qual, KEY)

cesm[, QUAL_KEY := norm(X001)]
missing_qual <- cesm[!QUAL_KEY %in% valid_qual$KEY,
                     .(StudentNo = X007, QualCode = QUAL_KEY, Reason = 'Qual code not in QUAL table')]

exceptions <- missing_qual
exceptions[, Status := 'FAIL']

print_summary(exceptions, 'Rule 15: Foundation CESM - Course Registration Check')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }
}
