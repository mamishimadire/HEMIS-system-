using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule20RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule20ValidationRequest req)
    {
        var m = req.ColumnMapping ?? new Rule20ColumnMapping();

        var studTable         = RString(req.StudTable);
        var qualTable         = RString(req.QualTable);
        var cregTable         = RString(req.CregTable);
        var crseTable         = RString(req.CrseTable);
        var studFoundationFlag= RString(m.StudFoundationFlag);
        var studFoundationVal = RString(m.StudFoundationValue);
        var studQualCode      = RString(m.StudQualCode);
        var cregQualCode      = RString(m.CregQualCode);
        var cregCourseCode    = RString(m.CregCourseCode);
        var crseCourseCode    = RString(m.CrseCourseCode);
        var crseFoundationFlag= RString(m.CrseFoundationFlag);
        var crseFoundationVal = RString(m.CrseFoundationValue);

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

stud_table <- '{studTable}'
qual_table <- '{qualTable}'
creg_table <- '{cregTable}'
crse_table <- '{crseTable}'
stud_foundation_flag  <- '{studFoundationFlag}'
stud_foundation_value <- '{studFoundationVal}'
stud_qual_col  <- '{studQualCode}'
creg_qual_col  <- '{cregQualCode}'
creg_crse_col  <- '{cregCourseCode}'
crse_crse_col  <- '{crseCourseCode}'
crse_fdn_flag  <- '{crseFoundationFlag}'
crse_fdn_value <- '{crseFoundationVal}'

stud <- copy(ds[[stud_table]]); safe_names(stud)
qual <- copy(ds[[qual_table]]); safe_names(qual)
creg <- copy(ds[[creg_table]]); safe_names(creg)
crse <- copy(ds[[crse_table]]); safe_names(crse)

sc_stud  <- gsub('^_', 'X', stud_foundation_flag)
sc_qual  <- gsub('^_', 'X', stud_qual_col)
sc_cregq <- gsub('^_', 'X', creg_qual_col)
sc_cregc <- gsub('^_', 'X', creg_crse_col)
sc_crsec <- gsub('^_', 'X', crse_crse_col)
sc_crsef <- gsub('^_', 'X', crse_fdn_flag)

force_char_trim(stud, c(sc_stud, sc_qual, 'X007'))
force_char_trim(creg, c(sc_cregq, sc_cregc))
force_char_trim(crse, c(sc_crsec, sc_crsef))

fdn_students <- stud[norm(col_val(.SD, sc_stud)) == norm(stud_foundation_value)]
fdn_crse     <- crse[norm(col_val(.SD, sc_crsef)) == norm(crse_fdn_value),
                     .(CRSE_CODE = norm(col_val(.SD, sc_crsec)))]
setkey(fdn_crse, CRSE_CODE)

creg[, CREG_QUAL  := norm(col_val(.SD, sc_cregq))]
creg[, CREG_CRSE  := norm(col_val(.SD, sc_cregc))]
creg[, CRSE_MATCH := CREG_CRSE %in% fdn_crse$CRSE_CODE]

fdn_students[, STUD_QUAL := norm(col_val(.SD, sc_qual))]
result <- merge(fdn_students[, .(StudentNo = X007, STUD_QUAL)],
                creg[, .(CREG_QUAL, CREG_CRSE, CRSE_MATCH)],
                by.x = 'STUD_QUAL', by.y = 'CREG_QUAL', all.x = TRUE)

result[, Status := fcase(
  is.na(CREG_CRSE), 'FAIL - No CREG record',
  !CRSE_MATCH,      'FAIL - Course not a foundation course',
  default = 'PASS'
)]

exceptions <- result[Status != 'PASS']
print_summary(result, 'Rule 20: Foundation Student Validation')
cat(sprintf('Exceptions: %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions)
";
    }
}
