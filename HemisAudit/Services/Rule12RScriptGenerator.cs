using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule12RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule12ValidationRequest req)
    {
        var cregTable         = RString(req.CregTable);
        var qualTable         = RString(req.QualTable);
        var cresTable         = RString(req.CresTable);
        var cregStudentCol    = RString(req.CregStudentCol);
        var cregQualCol       = RString(req.CregQualCol);
        var cregCourseCol     = RString(req.CregCourseCol);
        var qualJoinCol       = RString(req.QualJoinCol);
        var qualDescCol       = RString(req.QualDescCol);
        var cresCourseCol     = RString(req.CresCourseCol);
        var cresStatusCol     = RString(req.CresStatusCol);
        var cresStatusFilter  = RString(req.CresStatusFilter);
        var cresExtra1Col     = RString(req.CresExtra1Col);

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

creg_table        <- '{cregTable}'
qual_table        <- '{qualTable}'
cres_table        <- '{cresTable}'
creg_student_col  <- '{cregStudentCol}'
creg_qual_col     <- '{cregQualCol}'
creg_course_col   <- '{cregCourseCol}'
qual_join_col     <- '{qualJoinCol}'
qual_desc_col     <- '{qualDescCol}'
cres_course_col   <- '{cresCourseCol}'
cres_status_col   <- '{cresStatusCol}'
cres_status_filter<- '{cresStatusFilter}'
cres_extra1_col   <- '{cresExtra1Col}'

creg <- copy(ds[[creg_table]])
qual <- copy(ds[[qual_table]])
cres <- copy(ds[[cres_table]])

force_char_trim(cres, c(cres_course_col, cres_status_col))
force_char_trim(creg, c(creg_student_col, creg_qual_col, creg_course_col))
force_char_trim(qual, c(qual_join_col, qual_desc_col))

cres_active <- cres[norm(col_val(.SD, cres_status_col)) == norm(cres_status_filter)]
setnames(cres_active, cres_course_col, 'CRES_COURSE')
if (nchar(cres_extra1_col) > 0 && cres_extra1_col %in% names(cres_active))
  setnames(cres_active, cres_extra1_col, 'CRES_EXTRA1')
cres_keys <- unique(cres_active[, .(CRES_COURSE = norm(CRES_COURSE))])
setkey(cres_keys, CRES_COURSE)

setnames(qual, qual_join_col, 'QUAL_KEY')
setnames(qual, qual_desc_col, 'QUAL_DESC')
qual_lookup <- unique(qual[!is.na(QUAL_KEY) & QUAL_KEY != '', .(QUAL_KEY = norm(QUAL_KEY), QUAL_DESC)])
setkey(qual_lookup, QUAL_KEY)

setnames(creg, creg_student_col, 'STUD_NO')
setnames(creg, creg_qual_col,    'QUAL_CODE')
setnames(creg, creg_course_col,  'COURSE_CODE')
creg[, STUD_NO    := norm(STUD_NO)]
creg[, QUAL_CODE  := norm(QUAL_CODE)]
creg[, COURSE_CODE:= norm(COURSE_CODE)]

creg[, CRES_MATCH := COURSE_CODE %in% cres_keys$CRES_COURSE]
creg[, QUAL_MATCH := QUAL_CODE   %in% qual_lookup$QUAL_KEY]
creg[qual_lookup, QUAL_DESC := i.QUAL_DESC, on = .(QUAL_CODE = QUAL_KEY)]

creg[, Status := fcase(
  !CRES_MATCH, 'FAIL - Course not in active CRES',
  !QUAL_MATCH, 'FAIL - Qual not in QUAL table',
  default = 'PASS'
)]

exceptions <- creg[Status != 'PASS']
print_summary(creg, 'Rule 12: Active Course Selection')
cat(sprintf('Exceptions (FAIL rows): %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions[, .(STUD_NO, QUAL_CODE, COURSE_CODE, QUAL_DESC, Status)])
";
    }
}
