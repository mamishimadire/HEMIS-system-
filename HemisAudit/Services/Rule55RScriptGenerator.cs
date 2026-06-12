using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule55RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule55ValidationRequest req)
    {
        var studTable           = RString(req.StudTable);
        var qualTable           = RString(req.QualTable);
        var studIdCol           = RString(req.StudIdCol);
        var studQualCodeCol     = RString(req.StudQualCodeCol);
        var studFulfilledCol    = RString(req.StudFulfilledCol);
        var studFulfilledVal    = RString(req.StudFulfilledFilterValue);
        var qualCodeCol         = RString(req.QualCodeCol);
        var qualNameCol         = RString(req.QualNameCol);
        var qualApprovalCol     = RString(req.QualApprovalCol);
        var qualApprovalVal     = RString(req.QualApprovalFilterValue);

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

stud_table         <- '{studTable}'
qual_table         <- '{qualTable}'
stud_id_col        <- '{studIdCol}'
stud_qual_code_col <- '{studQualCodeCol}'
stud_fulfilled_col <- '{studFulfilledCol}'
stud_fulfilled_val <- '{studFulfilledVal}'
qual_code_col      <- '{qualCodeCol}'
qual_name_col      <- '{qualNameCol}'
qual_approval_col  <- '{qualApprovalCol}'
qual_approval_val  <- '{qualApprovalVal}'

stud <- copy(ds[[stud_table]]); safe_names(stud)
qual <- copy(ds[[qual_table]]); safe_names(qual)

sic <- gsub('^_', 'X', stud_id_col)
sqc <- gsub('^_', 'X', stud_qual_code_col)
sfc <- gsub('^_', 'X', stud_fulfilled_col)
qqc <- gsub('^_', 'X', qual_code_col)
qnc <- gsub('^_', 'X', qual_name_col)
qac <- gsub('^_', 'X', qual_approval_col)

force_char_trim(stud, c(sic, sqc, sfc))
force_char_trim(qual, c(qqc, qnc, qac))

approved_quals <- qual[norm(col_val(.SD, qac)) == norm(qual_approval_val),
                       .(QualKey = norm(col_val(.SD, qqc)), QualName = col_val(.SD, qnc))]
setkey(approved_quals, QualKey)

fulfilled <- stud[norm(col_val(.SD, sfc)) == norm(stud_fulfilled_val)]
fulfilled[, QUAL_KEY := norm(col_val(.SD, sqc))]

exceptions <- fulfilled[!QUAL_KEY %in% approved_quals$QualKey,
                         .(StudentId = col_val(.SD, sic), QualCode = QUAL_KEY,
                           Status = 'FAIL - Fulfilled student qual not in approved QUAL table')]

print_summary(exceptions, 'Rule 55: Graduate Fulfilled-Status')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }
}
