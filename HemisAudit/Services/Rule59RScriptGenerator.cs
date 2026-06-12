using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule59RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule59ValidationRequest req)
    {
        var valpacTable  = RString(req.ValpacTable);
        var prodTable    = RString(req.ProdTable);
        var valpacKeyCol = RString(req.ValpacCol037);
        var prodKeyCol   = RString(req.ProdColPersonelNumber);

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

valpac_table <- '{valpacTable}'
prod_table   <- '{prodTable}'
valpac_key   <- '{valpacKeyCol}'
prod_key     <- '{prodKeyCol}'

valpac <- copy(ds[[valpac_table]]); safe_names(valpac)
prod   <- copy(ds[[prod_table]]); safe_names(prod)

force_char_trim(valpac, c(gsub('^_', 'X', valpac_key)))
force_char_trim(prod,   c(prod_key))

vk <- gsub('^_', 'X', valpac_key)
valpac[, KEY_V := norm(col_val(.SD, vk))]
prod[,   KEY_P := norm(col_val(.SD, prod_key))]

in_valpac_not_prod <- valpac[!KEY_V %in% prod$KEY_P,
                              .(Key = KEY_V, Reason = paste('In', valpac_table, 'but not in', prod_table))]
in_prod_not_valpac <- prod[!KEY_P %in% valpac$KEY_V,
                            .(Key = KEY_P, Reason = paste('In', prod_table, 'but not in', valpac_table))]

exceptions <- rbindlist(list(in_valpac_not_prod, in_prod_not_valpac), use.names = TRUE)
exceptions[, Status := 'FAIL']

cat(sprintf('-- Rule 59: SFTE VALPAC Data vs SFTE PRODUCTION --\n'))
cat(sprintf('%s population : %d\n', valpac_table, nrow(valpac)))
cat(sprintf('%s population : %d\n', prod_table, nrow(prod)))
cat(sprintf('Exceptions   : %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions) else cat('No exceptions found.\n')
";
    }
}
