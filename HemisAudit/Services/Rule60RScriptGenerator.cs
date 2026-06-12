using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule60RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule41ValidationRequest req)
    {
        var table1  = RString(req.StudTable);
        var table2  = RString(req.AuditTable);
        var key1    = RString(req.StudKey);
        var key2    = RString(req.AuditKey);
        var pairs   = req.Pairs ?? new();
        var pairsR  = string.Join(",\n  ",
            pairs.Select(p => $"list(col1='{RString(p.StudCol)}', col2='{RString(p.AuditCol)}', label='{RString(p.Label)}')"));
        var pairsSection = string.IsNullOrEmpty(pairsR)
            ? $"list(col1='{key1}', col2='{key2}', label='Key')"
            : pairsR;

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

table1 <- '{table1}'
table2 <- '{table2}'
key1   <- '{key1}'
key2   <- '{key2}'

column_pairs <- list(
  {pairsSection}
)

tbl1 <- copy(ds[[table1]]); safe_names(tbl1)
tbl2 <- copy(ds[[table2]]); safe_names(tbl2)

k1 <- gsub('^_', 'X', key1)
k2 <- gsub('^_', 'X', key2)
force_char_trim(tbl1, c(k1))
force_char_trim(tbl2, c(k2))

tbl1[, KEY := norm(col_val(.SD, k1))]
tbl2[, KEY := norm(col_val(.SD, k2))]

result <- merge(tbl1, tbl2, by = 'KEY', all = TRUE, suffixes = c('_1', '_2'))

diff_cols <- character(0)
for (pair in column_pairs) {{
  c1 <- gsub('^_', 'X', pair$col1)
  c2 <- gsub('^_', 'X', pair$col2)
  c1r <- if (paste0(c1, '_1') %in% names(result)) paste0(c1, '_1') else c1
  c2r <- if (paste0(c2, '_2') %in% names(result)) paste0(c2, '_2') else c2
  if (c1r %in% names(result) && c2r %in% names(result)) {{
    dcol <- paste0('DIFF_', gsub('[^A-Za-z0-9]', '_', pair$label))
    result[, (dcol) := norm(col_val(.SD, c1r)) != norm(col_val(.SD, c2r))]
    diff_cols <- c(diff_cols, dcol)
  }}
}}

result[, Status := fcase(
  is.na(KEY), 'MISSING',
  !KEY %in% tbl2$KEY, paste0('MISSING-', table2),
  !KEY %in% tbl1$KEY, paste0('MISSING-', table1),
  length(diff_cols) > 0 && Reduce('|', lapply(diff_cols, function(c) !is.na(result[[c]]) & result[[c]])), 'DISAGREE',
  default = 'AGREE'
)]

cat(sprintf('-- Rule 60: CRSE vs H16CRSE Agreement --\n'))
tbl <- result[, .N, by = Status]
for (i in seq_len(nrow(tbl))) cat(sprintf('  %-20s: %d\n', tbl$Status[i], tbl$N[i]))
exceptions <- result[Status != 'AGREE']
if (nrow(exceptions) > 0) print(exceptions[, c('KEY', 'Status', diff_cols), with = FALSE])
";
    }
}
