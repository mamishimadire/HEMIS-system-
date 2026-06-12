using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule36RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(ValidationRequest req)
    {
        var studTable     = RString(req.StudTable);
        var deceasedTable = RString(req.DeceasedTable);
        var studCol       = RString(req.StudColumn);
        var deceasedCol   = RString(req.DeceasedColumn);

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

stud_table     <- '{studTable}'
deceased_table <- '{deceasedTable}'
stud_col       <- '{studCol}'
deceased_col   <- '{deceasedCol}'

stud     <- copy(ds[[stud_table]]);     safe_names(stud)
deceased <- copy(ds[[deceased_table]]); safe_names(deceased)

sc <- gsub('^_', 'X', stud_col)
dc <- gsub('^_', 'X', deceased_col)
force_char_trim(stud,     c(sc))
force_char_trim(deceased, c(dc))

stud_keys <- stud[!is.na(col_val(.SD, sc)) & col_val(.SD, sc) != '',
                   .(KEY = norm(col_val(.SD, sc)))]
setkey(stud_keys, KEY)

deceased[, KEY_DEC := norm(col_val(.SD, dc))]
still_active <- deceased[KEY_DEC %in% stud_keys$KEY,
                          .(DeceasedStudentNo = KEY_DEC, Reason = 'Deceased student still active in STUD')]
still_active[, Status := 'FAIL']
missing_from_stud <- deceased[!KEY_DEC %in% stud_keys$KEY,
                               .(DeceasedStudentNo = KEY_DEC, Reason = 'Deceased student not in STUD')]
missing_from_stud[, Status := 'INFO']

exceptions <- rbindlist(list(still_active, missing_from_stud), use.names = TRUE)
print_summary(exceptions[Status == 'FAIL'], 'Rule 36: STUD vs Deceased')
cat(sprintf('Still active (FAIL): %d\n', nrow(still_active)))
cat(sprintf('Not in STUD (INFO) : %d\n', nrow(missing_from_stud)))
if (nrow(still_active) > 0) print(still_active)
";
    }
}
