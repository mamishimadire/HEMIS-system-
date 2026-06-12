using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule44RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule44ValidationRequest req)
    {
        var m = req.ColumnMapping ?? new Rule44ColumnMapping();

        var studTable        = RString(req.StudTable);
        var qualTable        = RString(req.QualTable);
        var studStnoCol      = RString(m.StudStudentNoCol);
        var studQualCol      = RString(m.StudQualCodeCol);
        var studStatusCol    = RString(m.StudStatusCol);
        var studIdCol        = RString(m.StudIdCol);
        var studResearchCol  = RString(m.StudResearchTimeCol);
        var qualCodeCol      = RString(m.QualQualCodeCol);

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
qual_table        <- '{qualTable}'
stud_stno_col     <- '{studStnoCol}'
stud_qual_col     <- '{studQualCol}'
stud_status_col   <- '{studStatusCol}'
stud_id_col       <- '{studIdCol}'
stud_research_col <- '{studResearchCol}'
qual_code_col     <- '{qualCodeCol}'

stud <- copy(ds[[stud_table]]); safe_names(stud)
qual <- copy(ds[[qual_table]]); safe_names(qual)

sc  <- gsub('^_', 'X', stud_stno_col)
qc  <- gsub('^_', 'X', stud_qual_col)
st  <- gsub('^_', 'X', stud_status_col)
rc  <- gsub('^_', 'X', stud_research_col)
qqc <- gsub('^_', 'X', qual_code_col)

force_char_trim(stud, c(sc, qc, st, rc))
force_char_trim(qual, c(qqc))

valid_quals <- qual[!is.na(col_val(.SD, qqc)) & col_val(.SD, qqc) != '',
                    .(QualKey = norm(col_val(.SD, qqc)))]
setkey(valid_quals, QualKey)

stud[, QUAL_KEY := norm(col_val(.SD, qc))]
stud[, STATUS   := norm(col_val(.SD, st))]
graduated    <- stud[STATUS == 'G']
missing_qual <- graduated[!QUAL_KEY %in% valid_quals$QualKey,
                           .(StudentNo = col_val(.SD, sc), QualCode = QUAL_KEY,
                             Status = 'FAIL - Qual code not in QUAL table')]

exceptions <- missing_qual
print_summary(exceptions, 'Rule 44: Student Graduation Validation')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }
}
