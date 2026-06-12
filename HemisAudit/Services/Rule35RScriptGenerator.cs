using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule35RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule35ValidationRequest req)
    {
        var tableName     = RString(req.TableName);
        var duplicateCol  = RString(req.DuplicateColumn ?? "_030");

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

crse_table    <- '{tableName}'
duplicate_col <- '{duplicateCol}'

crse <- copy(ds[[crse_table]]); safe_names(crse)
dc <- gsub('^_', 'X', duplicate_col)
force_char_trim(crse, c(dc))

crse[, KEY := norm(col_val(.SD, dc))]
dupes <- crse[KEY != '' & !is.na(KEY), .(.N), by = KEY][N > 1]
setnames(dupes, 'KEY', 'CourseCode')

exceptions <- crse[norm(col_val(.SD, dc)) %in% dupes$CourseCode]
exceptions[, Status := 'FAIL - Duplicate course code']

print_summary(exceptions, 'Rule 35: Duplicate CRSE Course Code')
cat(sprintf('Duplicate groups : %d\n', nrow(dupes)))
cat(sprintf('Affected rows    : %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions[, c(dc, 'Status'), with = FALSE])
";
    }
}
