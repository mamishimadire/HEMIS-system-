using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule40RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule40ValidationRequest req)
    {
        var profTable  = RString(req.ProfTable);
        var otherTable = RString(req.ValpacTable);
        var profKey    = RString(req.ProfKey);
        var otherKey   = RString(req.ValpacKey);

        var pairsR = string.Join(",\n  ",
            (req.ValpacPairs ?? new()).Select(p =>
                $"list(prof='{p.ProfCol.Replace("'", "\\'")}', other='{p.OtherCol.Replace("'", "\\'")}', label='{p.Label.Replace("'", "\\'")}')"));
        if (string.IsNullOrEmpty(pairsR))
            pairsR = "list(prof='_037', other='_037', label='Staff No')";

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

prof_table  <- '{profTable}'
other_table <- '{otherTable}'
prof_key    <- '{profKey}'
other_key   <- '{otherKey}'

column_pairs <- list(
  {pairsR}
)

prof  <- copy(ds[[prof_table]]);  safe_names(prof)
other <- copy(ds[[other_table]]); safe_names(other)

pk_safe <- gsub('^_', 'X', prof_key)
ok_safe <- gsub('^_', 'X', other_key)
force_char_trim(prof,  c(pk_safe))
force_char_trim(other, c(ok_safe))

prof[,  KEY := norm(col_val(.SD, pk_safe))]
other[, KEY := norm(col_val(.SD, ok_safe))]
result <- merge(prof, other, by = 'KEY', all = TRUE, suffixes = c('_p', '_o'))
result[, MISSING_PROF  := is.na(col_val(.SD, paste0(pk_safe, '_p')))]
result[, MISSING_OTHER := is.na(col_val(.SD, paste0(ok_safe, '_o')))]

disagree_cols <- character(0)
for (pair in column_pairs) {{
  pc <- gsub('^_', 'X', pair$prof)
  oc <- gsub('^_', 'X', pair$other)
  pc_r <- if (paste0(pc, '_p') %in% names(result)) paste0(pc, '_p') else pc
  oc_r <- if (paste0(oc, '_o') %in% names(result)) paste0(oc, '_o') else oc
  if (pc_r %in% names(result) && oc_r %in% names(result)) {{
    col_name <- paste0('DIFF_', pair$label)
    result[, (col_name) := norm(col_val(.SD, pc_r)) != norm(col_val(.SD, oc_r))]
    disagree_cols <- c(disagree_cols, col_name)
  }}
}}

result[, Status := fcase(
  MISSING_PROF,  paste0('MISSING-', prof_table),
  MISSING_OTHER, paste0('MISSING-', other_table),
  length(disagree_cols) > 0 && Reduce('|', lapply(disagree_cols, function(c) result[[c]])), 'DISAGREE',
  default = 'AGREE'
)]

exceptions <- result[Status != 'AGREE']
print_summary(result, 'Rule 40: Staff Agreement')
cat(sprintf('Exceptions: %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions[, c('KEY', 'Status', disagree_cols), with = FALSE])
";
    }
}
