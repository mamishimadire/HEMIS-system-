using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule41RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule41ValidationRequest req, string ruleNumber = "41", string ruleTitle = "STUD vs MT-Audit Agreement")
    {
        var studTable  = RString(req.StudTable);
        var auditTable = RString(req.AuditTable);
        var studKey    = RString(req.StudKey);
        var auditKey   = RString(req.AuditKey);

        var defaultPairs = new List<Rule41ColumnPair>
        {
            new() { StudCol = "_007", AuditCol = "IAGSTNO", Label = "Student No" },
            new() { StudCol = "_008", AuditCol = "IADIDNO",  Label = "Birth Date" },
            new() { StudCol = "_001", AuditCol = "IAGQUAL",  Label = "Qualification" }
        };
        var pairsR = string.Join(",\n  ",
            (req.Pairs ?? defaultPairs).Select(p =>
                $"list(stud='{p.StudCol.Replace("'", "\\'")}', audit='{p.AuditCol.Replace("'", "\\'")}', label='{p.Label.Replace("'", "\\'")}')"));

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

stud_table  <- '{studTable}'
audit_table <- '{auditTable}'
stud_key    <- '{studKey}'
audit_key   <- '{auditKey}'

column_pairs <- list(
  {pairsR}
)

stud  <- copy(ds[[stud_table]]);  safe_names(stud)
audit <- copy(ds[[audit_table]]); safe_names(audit)

sk_safe <- gsub('^_', 'X', stud_key)
ak_safe <- audit_key
force_char_trim(stud,  c(sk_safe))
force_char_trim(audit, c(ak_safe))

stud[,  KEY := norm(col_val(.SD, sk_safe))]
audit[, KEY := norm(col_val(.SD, ak_safe))]
result <- merge(stud, audit, by = 'KEY', all = TRUE, suffixes = c('_s', '_a'))

agree_parts <- character(0)
for (pair in column_pairs) {{
  sc <- gsub('^_', 'X', pair$stud)
  ac <- pair$audit
  sc_r <- if (paste0(sc, '_s') %in% names(result)) paste0(sc, '_s') else sc
  ac_r <- if (paste0(ac, '_a') %in% names(result)) paste0(ac, '_a') else ac
  if (sc_r %in% names(result) && ac_r %in% names(result)) {{
    diff_col <- paste0('DIFF_', gsub(' ', '_', pair$label))
    result[, (diff_col) := norm(col_val(.SD, sc_r)) != norm(col_val(.SD, ac_r))]
    agree_parts <- c(agree_parts, diff_col)
  }}
}}

result[, Status := fcase(
  is.na(col_val(.SD, paste0(sk_safe, '_s'))), paste0('MISSING-', stud_table),
  is.na(col_val(.SD, paste0(ak_safe, '_a'))), paste0('MISSING-', audit_table),
  length(agree_parts) > 0 && Reduce('|', lapply(agree_parts, function(c) result[[c]])), 'DISAGREE',
  default = 'AGREE'
)]

exceptions <- result[Status != 'AGREE']
print_summary(result, 'Rule {ruleNumber}: {ruleTitle}')
cat(sprintf('AGREE    : %d\n', nrow(result[Status == 'AGREE'])))
cat(sprintf('DISAGREE : %d\n', nrow(result[Status == 'DISAGREE'])))
cat(sprintf('MISSING  : %d\n', nrow(result[grepl('^MISSING', Status)])))
if (nrow(exceptions) > 0) print(exceptions[, c('KEY', 'Status', agree_parts), with = FALSE])
";
    }
}
