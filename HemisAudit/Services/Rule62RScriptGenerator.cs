using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule62RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule62ValidationRequest req)
    {
        var m               = req.ColumnMapping ?? new Rule62ColumnMapping();
        var studTable       = RString(req.StudTable);
        var qualTable       = RString(req.QualTable);
        var studNoCol       = RString(m.StudStudentNoCol);
        var studQualCol     = RString(m.StudQualCodeCol);
        var studIdCol       = RString(m.StudIdCol);
        var postalCol       = RString(m.StudPostalCodeCol);
        var homePostCol     = RString(m.StudHomePostcodeCol);
        var entranceCatCol  = RString(m.StudEntranceCategoryCol);
        var secCompCol      = RString(m.StudSecondaryCompletionCol);
        var matricAggCol    = RString(m.StudMatricAggregateCol);
        var qualCodeCol     = RString(m.QualQualCodeCol);
        var qualTypeCol     = RString(m.QualTypeCol);
        var qualTypeCodes   = RString(req.QualTypeCodesText ?? "01, 11, 02, 03, 21, 22, 23");

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

is_valid_postcode <- function(x) {{
  x <- trimws(as.character(x))
  x %in% c('XXXX', 'YYYY', 'ZZZZ') | grepl('^[0-9]{{4}}$', x)
}}

stud_table       <- '{studTable}'
qual_table       <- '{qualTable}'
stud_no_col      <- '{studNoCol}'
stud_qual_col    <- '{studQualCol}'
stud_id_col      <- '{studIdCol}'
postal_col       <- '{postalCol}'
home_post_col    <- '{homePostCol}'
entrance_cat_col <- '{entranceCatCol}'
sec_comp_col     <- '{secCompCol}'
matric_agg_col   <- '{matricAggCol}'
qual_code_col    <- '{qualCodeCol}'
qual_type_col    <- '{qualTypeCol}'
qual_type_codes  <- norm(trimws(unlist(strsplit('{qualTypeCodes}', ','))))

stud <- copy(ds[[stud_table]]); safe_names(stud)
qual <- copy(ds[[qual_table]]); safe_names(qual)

sc  <- gsub('^_', 'X', stud_no_col)
sqc <- gsub('^_', 'X', stud_qual_col)
sic <- gsub('^_', 'X', stud_id_col)
pcc <- gsub('^_', 'X', postal_col)
hpc <- gsub('^_', 'X', home_post_col)
ecc <- gsub('^_', 'X', entrance_cat_col)
scc <- gsub('^_', 'X', sec_comp_col)
mac <- gsub('^_', 'X', matric_agg_col)
qcc <- gsub('^_', 'X', qual_code_col)
qtc <- gsub('^_', 'X', qual_type_col)

force_char_trim(stud, c(sc, sqc, sic, pcc, hpc, ecc, scc, mac))
force_char_trim(qual, c(qcc, qtc))

qual[, QUAL_KEY  := norm(col_val(.SD, qcc))]
qual[, QUAL_TYPE := norm(col_val(.SD, qtc))]
qual_lookup <- qual[, .(QUAL_KEY, QUAL_TYPE)]

stud[, STUD_QUAL := norm(col_val(.SD, sqc))]
base <- merge(stud, qual_lookup, by.x = 'STUD_QUAL', by.y = 'QUAL_KEY', all.x = TRUE)
base[is.na(QUAL_TYPE), QUAL_TYPE := '']

base[, `:=`(
  StudentNo          = col_val(.SD, sc),
  QualCode           = col_val(.SD, sqc),
  StudentId          = col_val(.SD, sic),
  PostalPostcode     = norm(col_val(.SD, pcc)),
  HomePostcode       = norm(col_val(.SD, hpc)),
  EntranceCategory   = norm(col_val(.SD, ecc)),
  SecondaryCompletion= norm(col_val(.SD, scc)),
  MatricAggregate    = trimws(as.character(col_val(.SD, mac)))
)]

base[, IsConfiguredQualType := QUAL_TYPE %in% qual_type_codes]
base[, MatricAggregateInt := suppressWarnings(ifelse(grepl('^[0-9]+$', MatricAggregate), as.integer(MatricAggregate), NA_integer_))]
base[, ValLikeAggregate  := suppressWarnings(ifelse(grepl('^[0-9]+$', MatricAggregate), as.integer(MatricAggregate), 0L))]

base[, HasErr07201 := !is_valid_postcode(PostalPostcode)]
base[, HasErr01501 := !is_valid_postcode(HomePostcode)]
base[, HasErr02202 := SecondaryCompletion == 'ZZ' & EntranceCategory == 'F' & IsConfiguredQualType]
base[, HasErr02301 := MatricAggregate == '' | grepl('[^0-9]', MatricAggregate) | is.na(MatricAggregateInt) | (!is.na(MatricAggregateInt) & MatricAggregateInt > 2600L)]
base[, HasErr02302 := ValLikeAggregate <= 0L & EntranceCategory == 'F' & IsConfiguredQualType]

pass_rows <- base[HasErr07201 == FALSE & HasErr01501 == FALSE & HasErr02202 == FALSE & HasErr02301 == FALSE & HasErr02302 == FALSE,
  .(StudentNo, QualCode, StudentId, PostalPostcode, HomePostcode, EntranceCategory, SecondaryCompletion, MatricAggregate, QualType = QUAL_TYPE,
    ErrorCode = '', ValidationResult = 'PASS',
    ValidationExplanation = 'PASS: No Rule 62 SQLVALPAC exception code matched this STUD row.')]

err07201 <- base[HasErr07201 == TRUE,
  .(StudentNo, QualCode, StudentId, PostalPostcode, HomePostcode, EntranceCategory, SecondaryCompletion, MatricAggregate, QualType = QUAL_TYPE,
    ErrorCode = '07201', ValidationResult = 'FAIL',
    ValidationExplanation = ifelse(PostalPostcode == '',
      paste0('07201: STUD.', postal_col, ' is blank and does not resolve to a valid postcode or approved placeholder (XXXX, YYYY, ZZZZ).'),
      paste0('07201: STUD.', postal_col, ' (', PostalPostcode, ') is not a four-digit postcode or approved placeholder (XXXX, YYYY, ZZZZ).')))]

err01501 <- base[HasErr01501 == TRUE,
  .(StudentNo, QualCode, StudentId, PostalPostcode, HomePostcode, EntranceCategory, SecondaryCompletion, MatricAggregate, QualType = QUAL_TYPE,
    ErrorCode = '01501', ValidationResult = 'FAIL',
    ValidationExplanation = ifelse(HomePostcode == '',
      paste0('01501: STUD.', home_post_col, ' is blank and does not resolve to a valid postcode or approved placeholder (XXXX, YYYY, ZZZZ).'),
      paste0('01501: STUD.', home_post_col, ' (', HomePostcode, ') is not a four-digit postcode or approved placeholder (XXXX, YYYY, ZZZZ).')))]

err02202 <- base[HasErr02202 == TRUE,
  .(StudentNo, QualCode, StudentId, PostalPostcode, HomePostcode, EntranceCategory, SecondaryCompletion, MatricAggregate, QualType = QUAL_TYPE,
    ErrorCode = '02202', ValidationResult = 'FAIL',
    ValidationExplanation = paste0('02202: STUD.', sec_comp_col, ' = ZZ while STUD.', entrance_cat_col, ' = F and QUAL.', qual_type_col, ' (', ifelse(QUAL_TYPE == '', 'blank', QUAL_TYPE), ') is in the configured dashboard list.'))]

err02301 <- base[HasErr02301 == TRUE,
  .(StudentNo, QualCode, StudentId, PostalPostcode, HomePostcode, EntranceCategory, SecondaryCompletion, MatricAggregate, QualType = QUAL_TYPE,
    ErrorCode = '02301', ValidationResult = 'FAIL',
    ValidationExplanation = fcase(
      MatricAggregate == '', paste0('02301: STUD.', matric_agg_col, ' is blank and therefore outside the allowed 0 to 2600 integer range.'),
      grepl('[^0-9]', MatricAggregate), paste0('02301: STUD.', matric_agg_col, ' (', MatricAggregate, ') contains non-digit characters or signs.'),
      !is.na(MatricAggregateInt) & MatricAggregateInt > 2600L, paste0('02301: STUD.', matric_agg_col, ' (', MatricAggregate, ') is greater than 2600.'),
      default = paste0('02301: STUD.', matric_agg_col, ' (', MatricAggregate, ') is outside the allowed 0 to 2600 integer range.')))]

err02302 <- base[HasErr02302 == TRUE,
  .(StudentNo, QualCode, StudentId, PostalPostcode, HomePostcode, EntranceCategory, SecondaryCompletion, MatricAggregate, QualType = QUAL_TYPE,
    ErrorCode = '02302', ValidationResult = 'FAIL',
    ValidationExplanation = paste0('02302: STUD.', matric_agg_col, ' (', ifelse(MatricAggregate == '', 'blank', MatricAggregate), ') resolves to zero or less while STUD.', entrance_cat_col, ' = F and QUAL.', qual_type_col, ' (', ifelse(QUAL_TYPE == '', 'blank', QUAL_TYPE), ') is in the configured dashboard list.'))]

results <- rbindlist(list(pass_rows, err07201, err01501, err02202, err02301, err02302), use.names = TRUE, fill = TRUE)
results[, Status := ValidationResult]

print_summary(results, 'Rule 62: STUD Postal Code / Entrance Category Validation')
exceptions <- results[ValidationResult == 'FAIL']
cat(sprintf('Exceptions: %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions[, .(StudentNo, QualCode, ErrorCode, ValidationExplanation, ValidationResult)])
";
    }
}
