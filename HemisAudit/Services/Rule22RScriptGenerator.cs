using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule22RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule22ValidationRequest req)
    {
        var profTable = RString(req.ProfTable);

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

prof_table <- '{profTable}'
prof <- copy(ds[[prof_table]]); safe_names(prof)

required_cols <- c('X037', 'X011', 'X012', 'X013', 'X014')
missing_cols  <- setdiff(required_cols, names(prof))
if (length(missing_cols) > 0)
  cat(sprintf('WARNING: Missing columns in %s: %s\n', prof_table, paste(missing_cols, collapse = ', ')))

present_cols <- intersect(required_cols, names(prof))
force_char_trim(prof, present_cols)

prof[, StaffKey := norm(col_val(.SD, 'X037'))]
blank_key <- prof[is.na(StaffKey) | StaffKey == '', .(Reason = 'Blank staff number (X037)', StaffKey)]

exceptions <- blank_key
exceptions[, Status := 'FAIL']
print_summary(exceptions, 'Rule 22: Staff Validation')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }
}
