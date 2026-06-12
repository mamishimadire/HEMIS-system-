using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule57RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule57ValidationRequest req)
    {
        var studTable        = RString(req.StudTable);
        var cregTable        = RString(req.CregTable);
        var studIdCol        = RString(req.StudIdCol);
        var studRegTypeCol   = RString(req.StudRegTypeCol);
        var cregIdCol        = RString(req.CregIdCol);
        var cregRegTypeCol   = RString(req.CregRegTypeCol);
        var cregFilterVal    = RString(req.CregRegTypeFilterValue);

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

stud_table        <- '{studTable}'
creg_table        <- '{cregTable}'
stud_id_col       <- '{studIdCol}'
stud_reg_type_col <- '{studRegTypeCol}'
creg_id_col       <- '{cregIdCol}'
creg_reg_type_col <- '{cregRegTypeCol}'
creg_filter_val   <- '{cregFilterVal}'

stud <- copy(ds[[stud_table]]); safe_names(stud)
creg <- copy(ds[[creg_table]]); safe_names(creg)

sic  <- gsub('^_', 'X', stud_id_col)
src  <- gsub('^_', 'X', stud_reg_type_col)
cic  <- gsub('^_', 'X', creg_id_col)
crc  <- gsub('^_', 'X', creg_reg_type_col)

force_char_trim(stud, c(sic, src))
force_char_trim(creg, c(cic, crc))

creg_filtered <- if (nchar(creg_filter_val) > 0)
  creg[norm(col_val(.SD, crc)) == norm(creg_filter_val)]
else creg

creg_filtered[, CREG_KEY := norm(col_val(.SD, cic))]
creg_filtered[, CREG_REG := norm(col_val(.SD, crc))]
stud[, STUD_KEY := norm(col_val(.SD, sic))]
stud[, STUD_REG := norm(col_val(.SD, src))]

result <- merge(creg_filtered[, .(CREG_KEY, CREG_REG)],
                stud[, .(STUD_KEY, STUD_REG)],
                by.x = 'CREG_KEY', by.y = 'STUD_KEY', all.x = TRUE)

result[, Status := fcase(
  is.na(STUD_REG),      'FAIL - Student not in STUD',
  CREG_REG != STUD_REG, 'FAIL - Registration type mismatch',
  default = 'PASS'
)]

exceptions <- result[Status != 'PASS']
print_summary(result, 'Rule 57: Registration Documentation Agreement')
cat(sprintf('Exceptions: %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions)
";
    }
}
