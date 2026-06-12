using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule24RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule24ValidationRequest req)
    {
        var table1 = RString(req.QualTable);
        var table2 = RString(req.AuditTable);
        var table3 = RString(req.H16Table);

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

# Rule 24: Reconcile Qualification Datasets
tables <- Filter(nzchar, list(
  '{table1}',
  '{table2}',
  '{table3}'
))

cat(sprintf('-- Rule 24: Reconcile Qualification Datasets --\n'))
for (tbl_name in tables) {{
  if (table_exists(tbl_name)) {{
    cat(sprintf('%-30s : %d rows\n', tbl_name, nrow(ds[[tbl_name]])))
  }} else {{
    cat(sprintf('%-30s : NOT FOUND\n', tbl_name))
  }}
}}
if (length(tables) >= 2) {{
  t1 <- if (table_exists(tables[[1]])) nrow(ds[[tables[[1]]]]) else 0
  t2 <- if (table_exists(tables[[2]])) nrow(ds[[tables[[2]]]]) else 0
  cat(sprintf('\nRatio t2/t1: %.2f\n', t2 / max(t1, 1)))
}}
";
    }
}
