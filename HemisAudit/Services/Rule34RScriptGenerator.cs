using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule34RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule34ValidationRequest req)
    {
        var tableName      = RString(req.TableName);
        var firstDayCol    = RString(req.FirstDayColumn);
        var lastDayCol     = RString(req.LastDayColumn);
        var censusDateCol  = RString(req.CensusDateColumn);
        var blockCol       = RString(req.BlockColumn);

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

creg_table      <- '{tableName}'
first_day_col   <- '{firstDayCol}'
last_day_col    <- '{lastDayCol}'
census_date_col <- '{censusDateCol}'
block_col       <- '{blockCol}'

creg <- copy(ds[[creg_table]]); safe_names(creg)
fc <- gsub('^_', 'X', first_day_col)
lc <- gsub('^_', 'X', last_day_col)
cc <- gsub('^_', 'X', census_date_col)

force_char_trim(creg, c(fc, lc, cc))

creg[, FirstDay   := as.Date(col_val(.SD, fc), tryFormats = c('%Y-%m-%d', '%d/%m/%Y', '%d-%m-%Y'))]
creg[, LastDay    := as.Date(col_val(.SD, lc), tryFormats = c('%Y-%m-%d', '%d/%m/%Y', '%d-%m-%Y'))]
creg[, CensusDate := as.Date(col_val(.SD, cc), tryFormats = c('%Y-%m-%d', '%d/%m/%Y', '%d-%m-%Y'))]
creg[, PeriodDays  := as.numeric(LastDay - FirstDay)]
creg[, CensusDayNo := as.numeric(CensusDate - FirstDay) + 1L]

creg[, Status := fcase(
  is.na(CensusDate),                            'FAIL - Census date missing',
  is.na(FirstDay) | is.na(LastDay),             'FAIL - Period date missing',
  CensusDate < FirstDay | CensusDate > LastDay, 'FAIL - Census date outside period',
  default = 'PASS'
)]

exceptions <- creg[Status != 'PASS']
print_summary(creg, 'Rule 34: Census Date Validation')
cat(sprintf('Exceptions: %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0)
  print(exceptions[, .(FirstDay, LastDay, CensusDate, PeriodDays, CensusDayNo, Status)])
";
    }
}
