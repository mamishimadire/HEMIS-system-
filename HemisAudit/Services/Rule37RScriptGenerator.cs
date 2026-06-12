using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule37RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule37ValidationRequest req)
    {
        var cesmTable = RString(req.CesmTable);
        var qualTable = RString(req.QualTable);
        var pqmTable = RString(req.PqmTable);
        var cesmIdCol = RString(req.CesmIdCol);
        var cesmCodeCol = RString(req.CesmCodeCol);
        var qualIdCol = RString(req.QualIdCol);
        var qualNameCol = RString(req.QualNameCol);
        var pqmNameCol = RString(req.PqmNameCol);
        var pqmCode1Col = RString(req.PqmCode1Col);
        var pqmCode2Col = RString(string.IsNullOrWhiteSpace(req.PqmCode2Col) ? "CESM_Code2" : req.PqmCode2Col);

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

digits_only <- function(x) gsub('[^0-9]', '', trimws(as.character(x)))

trim_leading_zeros <- function(x) {{
  x <- trimws(as.character(x))
  trimmed <- sub('^0+', '', x)
  ifelse(nchar(trimmed) == 0, x, trimmed)
}}

norm_name <- function(x) gsub('\\s+', ' ', norm(x))

code_matches <- function(hemis_code, pqm_code, n = 4) {{
  hd <- digits_only(hemis_code)
  pd <- digits_only(pqm_code)
  if (!nzchar(hd) || !nzchar(pd)) return(FALSE)
  use <- min(n, nchar(hd), nchar(pd))
  use >= 2 && substr(hd, 1, use) == substr(pd, 1, use)
}}

review_reason <- function(hemis_code, pqm_code) {{
  hd <- digits_only(hemis_code)
  pd <- digits_only(pqm_code)
  if (!nzchar(hd) || !nzchar(pd)) return(NA_character_)

  hd_trim <- trim_leading_zeros(hd)
  pd_trim <- trim_leading_zeros(pd)

  if (nchar(hd) >= 4 && nchar(pd) >= 4 && substr(hd, 1, 4) == substr(pd, 1, 4)) return('first 4 digits matched')
  if (nchar(hd_trim) >= 4 && nchar(pd_trim) >= 4 && substr(hd_trim, 1, 4) == substr(pd_trim, 1, 4)) return('first 4 digits matched after removing leading zeros')
  if (nchar(hd) >= 3 && nchar(pd) >= 3 && substr(hd, 1, 3) == substr(pd, 1, 3)) return('first 3 digits matched')
  if (nchar(hd_trim) >= 3 && nchar(pd_trim) >= 3 && substr(hd_trim, 1, 3) == substr(pd_trim, 1, 3)) return('first 3 digits matched after removing leading zeros')

  NA_character_
}}

review_priority <- function(reason) {{
  match(reason, c(
    'first 4 digits matched',
    'first 4 digits matched after removing leading zeros',
    'first 3 digits matched',
    'first 3 digits matched after removing leading zeros'
  ), nomatch = 99L)
}}

print_summary <- function(dt, rule_label) {{
  cat(sprintf('\n-- %s --\n', rule_label))
  cat(sprintf('Total rows: %d\n', nrow(dt)))
  if ('Status' %in% names(dt)) {{
    tbl <- dt[, .N, by = Status]
    for (i in seq_len(nrow(tbl))) cat(sprintf('  %-16s: %d\n', tbl$Status[i], tbl$N[i]))
  }}
}}

cesm_table    <- '{cesmTable}'
qual_table    <- '{qualTable}'
pqm_table     <- '{pqmTable}'
cesm_id_col   <- '{cesmIdCol}'
cesm_code_col <- '{cesmCodeCol}'
qual_id_col   <- '{qualIdCol}'
qual_name_col <- '{qualNameCol}'
pqm_name_col  <- '{pqmNameCol}'
pqm_code1_col <- '{pqmCode1Col}'
pqm_code2_col <- '{pqmCode2Col}'

cesm <- copy(ds[[cesm_table]]); safe_names(cesm)
qual <- copy(ds[[qual_table]]); safe_names(qual)
pqm  <- copy(ds[[pqm_table]]); safe_names(pqm)

ci  <- gsub('^_', 'X', cesm_id_col)
cc  <- gsub('^_', 'X', cesm_code_col)
qi  <- gsub('^_', 'X', qual_id_col)
qn  <- gsub('^_', 'X', qual_name_col)
pn  <- gsub('^_', 'X', pqm_name_col)
pc1 <- gsub('^_', 'X', pqm_code1_col)
pc2 <- gsub('^_', 'X', pqm_code2_col)

force_char_trim(cesm, c(ci, cc))
force_char_trim(qual, c(qi, qn))
force_char_trim(pqm, c(pn, pc1, pc2))

base <- merge(
  cesm[, .(
    RecordId = trimws(as.character(col_val(.SD, ci))),
    HemisCesmCode = trimws(as.character(col_val(.SD, cc)))
  )],
  qual[, .(
    RecordId = trimws(as.character(col_val(.SD, qi))),
    HemisQualName = trimws(as.character(col_val(.SD, qn)))
  )],
  by = 'RecordId',
  all.x = TRUE
)

pqm_lookup <- pqm[, .(
  PqmCode1 = trimws(as.character(col_val(.SD, pc1))),
  PqmCode2 = trimws(as.character(col_val(.SD, pc2))),
  PqmName  = trimws(as.character(col_val(.SD, pn)))
)]

resolve_matched_code <- function(hemis_code, pqm_row) {{
  if (code_matches(hemis_code, pqm_row[['PqmCode1']])) return(pqm_row[['PqmCode1']])
  if (code_matches(hemis_code, pqm_row[['PqmCode2']])) return(pqm_row[['PqmCode2']])
  if (!is.na(pqm_row[['PqmCode1']]) && nzchar(trimws(as.character(pqm_row[['PqmCode1']]))) ) return(pqm_row[['PqmCode1']])
  pqm_row[['PqmCode2']]
}}

validate_record <- function(hemis_code, hemis_name) {{
  hemis_name_norm <- norm_name(hemis_name)

  code_mask <- vapply(seq_len(nrow(pqm_lookup)), function(i) {{
    code_matches(hemis_code, pqm_lookup$PqmCode1[i]) || code_matches(hemis_code, pqm_lookup$PqmCode2[i])
  }}, logical(1))
  code_rows <- pqm_lookup[code_mask]

  if (nrow(code_rows) > 0) {{
    combined <- code_rows[norm_name(PqmName) == hemis_name_norm]
    if (nrow(combined) > 0) {{
      best <- combined[1]
      matched_code <- resolve_matched_code(hemis_code, best)
      return(list(
        PqmCode = matched_code,
        PqmName = best$PqmName[1],
        CodeMatch = TRUE,
        NameMatch = TRUE,
        NeedsReview = FALSE,
        ValidationResult = 'PASS',
        ExceptionReason = ''
      ))
    }}

    best <- code_rows[1]
    matched_code <- resolve_matched_code(hemis_code, best)
    pqm_names <- paste(unique(code_rows$PqmName[nzchar(code_rows$PqmName)]), collapse = ' | ')
    return(list(
      PqmCode = matched_code,
      PqmName = best$PqmName[1],
      CodeMatch = TRUE,
      NameMatch = FALSE,
      NeedsReview = FALSE,
      ValidationResult = 'FAIL',
      ExceptionReason = paste0(
        ""Fail - qualification name did not align. HEMIS "", qual_name_col, "": '"", hemis_name,
        ""' | PQM "", pqm_name_col, "": '"", pqm_names,
        ""' | CESM "", cesm_code_col, "": '"", hemis_code,
        ""' | PQM matched code: '"", matched_code, ""'""
      )
    ))
  }}

  best_reason <- NA_character_
  best_code <- NA_character_
  best_name <- NA_character_
  best_priority <- 99L

  if (nrow(pqm_lookup) > 0) {{
    for (i in seq_len(nrow(pqm_lookup))) {{
      for (candidate in c(pqm_lookup$PqmCode1[i], pqm_lookup$PqmCode2[i])) {{
        reason <- review_reason(hemis_code, candidate)
        if (!is.na(reason)) {{
          priority <- review_priority(reason)
          if (is.na(best_reason) || priority < best_priority) {{
            best_reason <- reason
            best_code <- candidate
            best_name <- pqm_lookup$PqmName[i]
            best_priority <- priority
          }}
          if (!is.na(best_reason) && best_reason == 'first 4 digits matched') break
        }}
      }}
      if (!is.na(best_reason) && best_reason == 'first 4 digits matched') break
    }}
  }}

  if (!is.na(best_reason)) {{
    name_match <- identical(hemis_name_norm, norm_name(best_name))
    return(list(
      PqmCode = best_code,
      PqmName = best_name,
      CodeMatch = FALSE,
      NameMatch = name_match,
      NeedsReview = name_match,
      ValidationResult = ifelse(name_match, 'PASS', 'FAIL'),
      ExceptionReason = if (name_match) paste0(
        ""Pass - CESM review required because "", best_reason,
        "" against selected PQM CESM code columns while HEMIS "", qual_name_col, "" matched PQM "", pqm_name_col, "". "",
        ""HEMIS "", qual_name_col, "": '"", hemis_name,
        ""' | PQM "", pqm_name_col, "": '"", best_name,
        ""' | CESM "", cesm_code_col, "": '"", hemis_code,
        ""' | PQM matched code: '"", best_code, ""'""
      ) else paste0(
        ""Fail - CESM leading digits matched for review, but qualification name did not align. "",
        ""HEMIS "", qual_name_col, "": '"", hemis_name,
        ""' | PQM "", pqm_name_col, "": '"", best_name,
        ""' | CESM "", cesm_code_col, "": '"", hemis_code,
        ""' | PQM matched code: '"", best_code, ""'""
      )
    ))
  }}

  list(
    PqmCode = '',
    PqmName = '',
    CodeMatch = FALSE,
    NameMatch = FALSE,
    NeedsReview = FALSE,
    ValidationResult = 'FAIL',
    ExceptionReason = paste0(
      ""CESM code '"", hemis_code,
      ""' not found in PQM (no 4-digit prefix match in "", pqm_code1_col, "" or "", pqm_code2_col, "")""
    )
  )
}}

validated <- rbindlist(lapply(seq_len(nrow(base)), function(i) {{
  v <- validate_record(base$HemisCesmCode[i], base$HemisQualName[i])
  data.table(
    ValidationNumber = i,
    RecordId = base$RecordId[i],
    HemisCesmCode = base$HemisCesmCode[i],
    HemisQualName = base$HemisQualName[i],
    PqmCode = as.character(v$PqmCode),
    PqmName = as.character(v$PqmName),
    CodeMatch = isTRUE(v$CodeMatch),
    NameMatch = isTRUE(v$NameMatch),
    NeedsReview = isTRUE(v$NeedsReview),
    ValidationResult = as.character(v$ValidationResult),
    ExceptionReason = as.character(v$ExceptionReason)
  )
}}), fill = TRUE)

validated[is.na(PqmCode), PqmCode := '']
validated[is.na(PqmName), PqmName := '']
validated[is.na(ExceptionReason), ExceptionReason := '']
validated[, Status := fifelse(NeedsReview, 'PASS WITH REVIEW', ValidationResult)]

print_summary(validated, 'Rule 37: CESM/PQM Validation')

cat('\nFull validation result:\n')
print(validated[, .(
  ValidationNumber,
  RecordId,
  HemisCesmCode,
  HemisQualName,
  PqmCode,
  PqmName,
  CodeMatch,
  NameMatch,
  NeedsReview,
  ValidationResult,
  ExceptionReason
)])

exceptions <- validated[ValidationResult == 'FAIL' | NeedsReview]
cat('\nExceptions and review-required rows:\n')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions[, .(
  ValidationNumber,
  RecordId,
  HemisCesmCode,
  HemisQualName,
  PqmCode,
  PqmName,
  Status,
  ExceptionReason
)])
";
    }
}
