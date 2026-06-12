using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule17RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule17ValidationRequest req)
    {
        var tableName    = RString(req.TableName);
        var filterColumn = RString(req.FilterColumn);
        var filterValue  = RString(req.FilterValue);

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

# Rule 17: CESM - Qualification Link Validation
cesm_table <- '{tableName}'
pg_types   <- ''
governing  <- 'ALL'

cesm <- copy(ds[[cesm_table]]); safe_names(cesm)
filter_col <- gsub('^_', 'X', '{filterColumn}')
filter_val <- '{filterValue}'

force_char_trim(cesm, c('X001', 'X007'))

cat(sprintf('-- Rule 17: CESM - Qualification Link Validation --\n'))
cat(sprintf('Table: %s  Rows: %d\n', cesm_table, nrow(cesm)))

if (nchar(filter_col) > 0 && filter_col %in% names(cesm)) {{
  matched <- cesm[norm(col_val(.SD, filter_col)) == norm(filter_val)]
  cat(sprintf('Filter (%s == %s): %d rows\n', filter_col, filter_val, nrow(matched)))
}} else {{
  cat('No filter applied.\n')
}}
";
    }
}
