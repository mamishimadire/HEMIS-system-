using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule61RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule61ValidationRequest req)
    {
        var m               = req.ColumnMapping ?? new Rule61ColumnMapping();
        var studTable       = RString(req.StudTable);
        var qualTable       = RString(req.QualTable);
        var pqmTable        = RString(req.PqmTable);
        var studNoCol       = RString(m.StudStudentNoCol);
        var studStatusCol   = RString(m.StudStatusCol);
        var studQualCol     = RString(m.StudQualCodeCol);
        var studIdCol       = RString(m.StudIdCol);
        var studResearchCol = RString(m.StudResearchTimeCol);
        var qualCodeCol     = RString(m.QualQualCodeCol);
        var qualNameCol     = RString(m.QualNameCol);
        var qualTypeCol     = RString(m.QualTypeCol);
        var pqmNameCol      = RString(m.PqmNameCol);
        var pqmResearchCol  = RString(m.PqmResearchTimeCol);
        var statusValues    = RString(req.StudStatusValue ?? "N");
        var pgTypes         = RString(req.PgTypesText ?? "07,27,28,49,72,73,08,30,50,74,75");

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
    for (i in seq_len(nrow(tbl))) cat(sprintf('  %-20s: %d\n', tbl$Status[i], tbl$N[i]))
  }}
}}

stud_table       <- '{studTable}'
qual_table       <- '{qualTable}'
pqm_table        <- '{pqmTable}'
stud_no_col      <- '{studNoCol}'
stud_status_col  <- '{studStatusCol}'
stud_qual_col    <- '{studQualCol}'
stud_id_col      <- '{studIdCol}'
stud_research_col<- '{studResearchCol}'
qual_code_col    <- '{qualCodeCol}'
qual_name_col    <- '{qualNameCol}'
qual_type_col    <- '{qualTypeCol}'
pqm_name_col     <- '{pqmNameCol}'
pqm_research_col <- '{pqmResearchCol}'

status_values <- trimws(unlist(strsplit('{statusValues}', ',')))
pg_types      <- trimws(unlist(strsplit('{pgTypes}', ',')))

stud <- copy(ds[[stud_table]]); safe_names(stud)
qual <- copy(ds[[qual_table]]); safe_names(qual)
pqm  <- copy(ds[[pqm_table]]); safe_names(pqm)

sc  <- gsub('^_', 'X', stud_no_col)
ssc <- gsub('^_', 'X', stud_status_col)
sqc <- gsub('^_', 'X', stud_qual_col)
sic <- gsub('^_', 'X', stud_id_col)
src <- gsub('^_', 'X', stud_research_col)
qcc <- gsub('^_', 'X', qual_code_col)
qnc <- gsub('^_', 'X', qual_name_col)
qtc <- gsub('^_', 'X', qual_type_col)
pnc <- gsub('^_', 'X', pqm_name_col)
prc <- gsub('^_', 'X', pqm_research_col)

force_char_trim(stud, c(sc, ssc, sqc, sic, src))
force_char_trim(qual, c(qcc, qnc, qtc))
force_char_trim(pqm,  c(pnc, prc))

# Filter STUD by status values (e.g. 'N')
stud[, STUD_STATUS := norm(col_val(.SD, ssc))]
stud_pg <- stud[STUD_STATUS %in% norm(status_values)]

# Join STUD -> QUAL on qual code
stud_pg[, STUD_QUAL := norm(col_val(.SD, sqc))]
qual[, QUAL_KEY  := norm(col_val(.SD, qcc))]
qual[, QUAL_NAME := trimws(as.character(col_val(.SD, qnc)))]
qual[, QUAL_TYPE := norm(col_val(.SD, qtc))]

# Filter QUAL by PG type codes
qual_pg <- qual[QUAL_TYPE %in% norm(pg_types)]

# Join STUD_pg to QUAL_pg
joined <- merge(
  stud_pg[, .(
    StudentNo     = col_val(.SD, sc),
    StudentId     = col_val(.SD, sic),
    StudStatus    = STUD_STATUS,
    StudQualCode  = STUD_QUAL,
    StudResearch  = trimws(as.character(col_val(.SD, src)))
  )],
  qual_pg[, .(QUAL_KEY, QualName = QUAL_NAME, QualType = QUAL_TYPE)],
  by.x = 'StudQualCode', by.y = 'QUAL_KEY',
  all.x = TRUE
)

# Join to PQM on normalised qualification name
pqm[, PQM_NAME := norm(col_val(.SD, pnc))]
pqm[, PQM_RESEARCH := trimws(as.character(col_val(.SD, prc)))]
pqm_lookup <- pqm[, .(PQM_NAME, PqmResearch = PQM_RESEARCH)]

joined[, QUAL_NAME_NORM := norm(QualName)]
joined <- merge(joined, pqm_lookup, by.x = 'QUAL_NAME_NORM', by.y = 'PQM_NAME', all.x = TRUE)

joined[, ValidationResult := fcase(
  is.na(PqmResearch) | PqmResearch == '', 'MISSING_PQM',
  StudResearch == PqmResearch, 'PASS',
  default = 'FAIL'
)]

joined[, ValidationExplanation := fcase(
  ValidationResult == 'MISSING_PQM',
    paste0('No PQM record found for QUAL.', qual_name_col, ': ', QualName),
  ValidationResult == 'PASS',
    paste0('PASS: QUAL.', qual_name_col, ' (', QualName, ') matches PQM.', pqm_name_col,
           ' and STUD.', stud_research_col, ' (', StudResearch, ') agrees with PQM.', pqm_research_col,
           ' (', PqmResearch, ')'),
  default =
    paste0('FAIL: QUAL.', qual_name_col, ' (', QualName, ') matches PQM.', pqm_name_col,
           ' but STUD.', stud_research_col, ' (', StudResearch, ') disagrees with PQM.', pqm_research_col,
           ' (', PqmResearch, ')')
)]

joined[, Status := ValidationResult]

print_summary(joined, 'Rule 61: Masters/Doctoral Research Time Validation')

cat('\nFull results:\n')
print(joined[, .(StudentNo, StudQualCode, QualName, QualType, StudResearch, PqmResearch, ValidationResult, ValidationExplanation)])

exceptions <- joined[ValidationResult != 'PASS']
cat(sprintf('\nExceptions: %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions[, .(StudentNo, StudQualCode, QualName, StudResearch, PqmResearch, ValidationResult, ValidationExplanation)])
";
    }
}
