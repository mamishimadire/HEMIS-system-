using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule63RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule63ValidationRequest req)
    {
        var m = req.ColumnMapping ?? new Rule63ColumnMapping();

        var studTable = RString(req.StudTable);
        var qualTable = RString(req.QualTable);
        var cregTable = RString(req.CregTable);

        var studNoCol = RString(m.StudStudentNoCol);
        var studQualCol = RString(m.StudQualCodeCol);
        var cregNoCol = RString(m.CregStudentNoCol);
        var cregQualCol = RString(m.CregQualCodeCol);
        var qualCodeCol = RString(m.QualQualCodeCol);

        return RScriptScaffold.BuildDataLoadingPrelude() + $@"
options(stringsAsFactors = FALSE)

norm <- function(x) toupper(trimws(as.character(x)))

safe_names <- function(dt) {{
  setnames(dt, old = names(dt), new = gsub('^_', 'X', names(dt)))
  invisible(dt)
}}

force_char_trim <- function(dt, cols) {{
  for (col in cols) {{
    if (col %in% names(dt)) {{
      set(dt, j = col, value = trimws(as.character(dt[[col]])))
      set(dt, i = which(is.na(dt[[col]]) | dt[[col]] == 'NA'), j = col, value = '')
    }}
  }}
  invisible(dt)
}}

col_val <- function(dt, col, default = '') {{
  if (!is.null(col) && nzchar(col) && col %in% names(dt)) {{
    trimws(as.character(dt[[col]]))
  }} else {{
    rep(default, nrow(dt))
  }}
}}

print_summary <- function(dt, rule_label) {{
  cat(sprintf('\n=== %s ===\n', rule_label))
  cat(sprintf('Total rows: %d\n', nrow(dt)))
  if ('ValidationResult' %in% names(dt)) {{
    tbl <- dt[, .N, by = ValidationResult][order(ValidationResult)]
    for (i in seq_len(nrow(tbl))) {{
      cat(sprintf('  %-6s : %d\n', tbl$ValidationResult[i], tbl$N[i]))
    }}
  }}
}}

stud_table    <- '{studTable}'
qual_table    <- '{qualTable}'
creg_table    <- '{cregTable}'
stud_no_col   <- '{studNoCol}'
stud_qual_col <- '{studQualCol}'
creg_no_col   <- '{cregNoCol}'
creg_qual_col <- '{cregQualCol}'
qual_code_col <- '{qualCodeCol}'

output_file <- file.path(default_data_dir, 'Rule63_Qualification_Code_Reference_Validation.xlsx')

# ============================================================
# RULE 63: Qualification Code Reference Validation
# Logic:
#   1. Build a reference list from QUAL using the selected qualification code column.
#   2. Select non-blank qualification codes from the selected STUD table.
#   3. Select non-blank qualification codes from the selected CREG table.
#   4. PASS when the qualification code exists in QUAL.
#   5. FAIL with 00103 / E001 when the qualification code is not found in QUAL.
# ============================================================

STUD <- copy(ds[[stud_table]]); safe_names(STUD)
QUAL <- copy(ds[[qual_table]]); safe_names(QUAL)
CREG <- copy(ds[[creg_table]]); safe_names(CREG)

stud_no_key   <- gsub('^_', 'X', stud_no_col)
stud_qual_key <- gsub('^_', 'X', stud_qual_col)
creg_no_key   <- gsub('^_', 'X', creg_no_col)
creg_qual_key <- gsub('^_', 'X', creg_qual_col)
qual_code_key <- gsub('^_', 'X', qual_code_col)

force_char_trim(STUD, c(stud_no_key, stud_qual_key))
force_char_trim(CREG, c(creg_no_key, creg_qual_key))
force_char_trim(QUAL, c(qual_code_key))

# ============================================================
# STEP 1: Build reference qualification codes from QUAL
# ============================================================
ReferenceQuals <- unique(
  data.table(
    QualJoinCode = norm(col_val(QUAL, qual_code_key))
  )[QualJoinCode != '']
)
setkey(ReferenceQuals, QualJoinCode)

# ============================================================
# STEP 2: Build validation populations from STUD and CREG
# ============================================================
StudPopulation <- data.table(
  SourceTable = 'STUD',
  SourceColumn = stud_qual_col,
  StudentNo = col_val(STUD, stud_no_key),
  QualCode = norm(col_val(STUD, stud_qual_key))
)[QualCode != '']

CregPopulation <- data.table(
  SourceTable = 'CREG',
  SourceColumn = creg_qual_col,
  StudentNo = col_val(CREG, creg_no_key),
  QualCode = norm(col_val(CREG, creg_qual_key))
)[QualCode != '']

ValidationPopulation <- rbindlist(list(StudPopulation, CregPopulation), use.names = TRUE, fill = TRUE)

# ============================================================
# STEP 3: Validate qualification codes against QUAL
# ============================================================
results <- merge(
  ValidationPopulation,
  ReferenceQuals,
  by.x = 'QualCode',
  by.y = 'QualJoinCode',
  all.x = TRUE,
  sort = FALSE
)

results[is.na(QualJoinCode), QualJoinCode := '']
results[, ErrorCode := fifelse(QualJoinCode == '', '00103', '')]
results[, ValidationResult := fifelse(QualJoinCode == '', 'FAIL', 'PASS')]
results[, ValidationExplanation := fifelse(
  ValidationResult == 'FAIL',
  paste0(
    '00103 / E001: ', SourceTable, '.', SourceColumn,
    ' qualification code ''', QualCode, ''' was not found in ',
    qual_table, '.', qual_code_col, '.'
  ),
  paste0(
    'PASS: ', SourceTable, '.', SourceColumn,
    ' qualification code ''', QualCode, ''' exists in ',
    qual_table, '.', qual_code_col, '.'
  )
)]
results[, Status := ValidationResult]
results[, SortBucket := fifelse(ValidationResult == 'FAIL', 0L, 1L)]
data.table::setorder(results, SortBucket, SourceTable, StudentNo, QualCode)
results[, SortBucket := NULL]
results[, RowNumber := .I]

pass_rows <- results[ValidationResult == 'PASS']
exceptions <- results[ValidationResult == 'FAIL']
fail_rows <- copy(exceptions)

summary_table <- data.table(
  Metric = c('Total Rows Tested', 'Pass Rows', 'Fail Rows', 'Exception Rate %', 'Overall Status'),
  Value = c(
    as.character(nrow(results)),
    as.character(nrow(pass_rows)),
    as.character(nrow(exceptions)),
    sprintf('%.2f', if (nrow(results) == 0) 0 else (nrow(exceptions) / nrow(results)) * 100),
    if (nrow(exceptions) == 0) 'PASS' else 'FAIL'
  )
)

# ============================================================
# STEP 4: Output
# ============================================================
cat('=== RULE 63 SUMMARY ===\n')
cat('Total rows tested: ', nrow(results), '\n', sep = '')
cat('Pass rows:         ', nrow(pass_rows), '\n', sep = '')
cat('Fail rows:         ', nrow(exceptions), '\n', sep = '')
cat('Overall status:    ', ifelse(nrow(exceptions) == 0, 'PASS', 'FAIL'), '\n\n', sep = '')

print_summary(results, 'Rule 63 Validation Results')

cat('\n=== RULE 63 EXCEPTIONS ===\n')
if (nrow(exceptions) == 0) {{
  cat('No exceptions found.\n')
}} else {{
  print(exceptions[, .(
    RowNumber,
    SourceTable,
    StudentNo,
    QualCode,
    QualJoinCode,
    ErrorCode,
    ValidationResult,
    ValidationExplanation
  )])
}}
";
    }
}
