using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule32RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule32ValidationRequest req)
    {
        var tableName = RString(req.TableName);

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

# Rule 32: Error Filter
stud_table <- '{tableName}'
stud <- copy(ds[[stud_table]]); safe_names(stud)

err_cols <- grep('^X?ERR|_ERR|^ERR', names(stud), value = TRUE, ignore.case = TRUE)
if (length(err_cols) == 0) {{
  cat('No error columns found in', stud_table, '\n')
}} else {{
  error_rows <- stud[Reduce('|', lapply(err_cols, function(c) !is.na(stud[[c]]) & stud[[c]] != '' & stud[[c]] != '0'))]
  error_rows[, ErrorCount := rowSums(sapply(err_cols, function(c) !is.na(.SD[[c]]) & .SD[[c]] != '' & .SD[[c]] != '0'))]
  cat(sprintf('-- Rule 32: Error Filter --\n'))
  cat(sprintf('Total rows with errors: %d / %d\n', nrow(error_rows), nrow(stud)))
  key_col <- if ('X007' %in% names(error_rows)) 'X007' else names(error_rows)[1]
  if (nrow(error_rows) > 0) print(error_rows[, .SD, .SDcols = c(key_col, err_cols, 'ErrorCount')])
}}
";
    }
}
