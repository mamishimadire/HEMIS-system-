using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule38RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule38ValidationRequest req)
    {
        var qualTable = RString(req.QualTable);
        var pqmTable = RString(req.PqmTable);
        var qualIdCol = RString(req.QualIdCol);
        var qualNameCol = RString(req.QualNameCol);
        var qualApprovalCol = RString(req.QualApprovalCol);
        var qualApprovalValue = RString(req.QualApprovalValue ?? "A");
        var qualTypeCol = RString(req.QualTypeCol);
        var qualMinTimeTotalCol = RString(req.QualMinTimeTotalCol);
        var qualMinTimeWilCol = RString(req.QualMinTimeWilCol);
        var qualHeqfCol = RString(req.QualHeqfCol);
        var qualTotalSubsidyCol = RString(req.QualTotalSubsidyCol);
        var pqmNameCol = RString(req.PqmNameCol);
        var pqmQualTypeCol = RString(req.PqmQualTypeCol);
        var pqmMinTimeTotalCol = RString(req.PqmMinTimeTotalCol);
        var pqmWilCol = RString(req.PqmWilCol);
        var pqmAccreditationCol = RString(req.PqmAccreditationCol);
        var pqmTotalSubsidyCol = RString(req.PqmTotalSubsidyCol);
        var heqfCodesCsv = RString(req.HeqfIndicatorCodesCsv);

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

numeric_match <- function(a, b) {{
  left  <- trimws(as.character(a))
  right <- trimws(as.character(b))

  if (!nzchar(left) && !nzchar(right)) return(TRUE)
  if (!nzchar(left) || !nzchar(right)) return(FALSE)

  left_num  <- suppressWarnings(as.numeric(left))
  right_num <- suppressWarnings(as.numeric(right))
  if (!is.na(left_num) && !is.na(right_num)) return(left_num == right_num)

  identical(toupper(left), toupper(right))
}}

heqf_codes <- trimws(unlist(strsplit('{heqfCodesCsv}', ',', fixed = TRUE)))
heqf_codes <- heqf_codes[nzchar(heqf_codes)]

is_heqf_indicated <- function(accreditation_ref) {{
  upper <- toupper(trimws(as.character(accreditation_ref)))
  if (!nzchar(upper)) return(FALSE)
  any(vapply(heqf_codes, function(code) nzchar(code) && grepl(toupper(code), upper, fixed = TRUE), logical(1)))
}}

qual <- copy(ds[['{qualTable}']]); safe_names(qual)
pqm  <- copy(ds[['{pqmTable}']]);  safe_names(pqm)

qi  <- gsub('^_', 'X', '{qualIdCol}')
qn  <- gsub('^_', 'X', '{qualNameCol}')
qa  <- gsub('^_', 'X', '{qualApprovalCol}')
qt  <- gsub('^_', 'X', '{qualTypeCol}')
q53 <- gsub('^_', 'X', '{qualMinTimeTotalCol}')
q54 <- gsub('^_', 'X', '{qualMinTimeWilCol}')
q84 <- gsub('^_', 'X', '{qualHeqfCol}')
q90 <- gsub('^_', 'X', '{qualTotalSubsidyCol}')

pn  <- gsub('^_', 'X', '{pqmNameCol}')
pt  <- gsub('^_', 'X', '{pqmQualTypeCol}')
p53 <- gsub('^_', 'X', '{pqmMinTimeTotalCol}')
p54 <- gsub('^_', 'X', '{pqmWilCol}')
p84 <- gsub('^_', 'X', '{pqmAccreditationCol}')
p90 <- gsub('^_', 'X', '{pqmTotalSubsidyCol}')

force_char_trim(qual, c(qi, qn, qa, qt, q53, q54, q84, q90))
force_char_trim(pqm, c(pn, pt, p53, p54, p84, p90))

qual[, `:=`(
  QualCode       = norm(get(qi)),
  QualName       = trimws(as.character(get(qn))),
  ApprovalStatus = norm(get(qa)),
  QualType       = trimws(as.character(get(qt))),
  MinTimeTotal   = trimws(as.character(get(q53))),
  MinTimeWIL     = trimws(as.character(get(q54))),
  HeqfIndicator  = norm(get(q84)),
  TotalSubsidy   = trimws(as.character(get(q90)))
)]

pqm[, `:=`(
  PqmName         = trimws(as.character(get(pn))),
  PqmQualType     = trimws(as.character(get(pt))),
  PqmMinTimeTotal = trimws(as.character(get(p53))),
  PqmWIL          = trimws(as.character(get(p54))),
  PqmAccreditation= trimws(as.character(get(p84))),
  PqmTotalSubsidy = trimws(as.character(get(p90)))
)]

approved <- qual[ApprovalStatus == norm('{qualApprovalValue}')]
approved[, MatchName := norm(QualName)]
approved[, MatchType := norm(QualType)]

pqm_lookup <- copy(pqm)
pqm_lookup[, MatchName := norm(PqmName)]
pqm_lookup[, MatchType := norm(PqmQualType)]

matched <- merge(
  approved,
  pqm_lookup[, .(MatchName, MatchType, PqmName, PqmQualType, PqmMinTimeTotal, PqmWIL, PqmAccreditation, PqmTotalSubsidy)],
  by = c('MatchName', 'MatchType'),
  all.x = TRUE,
  allow.cartesian = TRUE
)

matched[, MatchRank := seq_len(.N), by = QualCode]
matched <- matched[MatchRank == 1]

matched[, HasPqmMatch := !is.na(PqmName) & trimws(as.character(PqmName)) != '']
matched[, C2_TypeMatch := HasPqmMatch]
matched[, C3_MinTimeMatch := mapply(function(has_match, a, b) if (isTRUE(has_match)) numeric_match(a, b) else FALSE, HasPqmMatch, MinTimeTotal, PqmMinTimeTotal)]
matched[, C4_WILMatch := mapply(function(has_match, a, b) if (isTRUE(has_match)) numeric_match(a, b) else FALSE, HasPqmMatch, MinTimeWIL, PqmWIL)]
matched[, C5_ExpectedHeqf := ifelse(vapply(PqmAccreditation, is_heqf_indicated, logical(1)), 'Y', 'N')]
matched[, C5_HeqfMatch := ifelse(HasPqmMatch, norm(HeqfIndicator) == C5_ExpectedHeqf, FALSE)]
matched[, C6_SubsidyMatch := mapply(function(has_match, a, b) if (isTRUE(has_match)) numeric_match(a, b) else FALSE, HasPqmMatch, TotalSubsidy, PqmTotalSubsidy)]

matched[, FailedControls := apply(.SD, 1, function(row) {{
  failed <- character()
  if (row[['HasPqmMatch']] != 'TRUE') failed <- c(failed, 'No PQM row matched QUAL name and qualification type')
  if (row[['C2_TypeMatch']] != 'TRUE') failed <- c(failed, 'C2')
  if (row[['C3_MinTimeMatch']] != 'TRUE') failed <- c(failed, 'C3')
  if (row[['C4_WILMatch']] != 'TRUE') failed <- c(failed, 'C4')
  if (row[['C5_HeqfMatch']] != 'TRUE') failed <- c(failed, 'C5')
  if (row[['C6_SubsidyMatch']] != 'TRUE') failed <- c(failed, 'C6')
  paste(failed, collapse = '; ')
}}), .SDcols = c('HasPqmMatch', 'C2_TypeMatch', 'C3_MinTimeMatch', 'C4_WILMatch', 'C5_HeqfMatch', 'C6_SubsidyMatch')]

matched[, MatchNote := ifelse(
  HasPqmMatch,
  'Matched PQM on qualification name and qualification type. Rule 38 no longer depends on PQM CESM columns.',
  'No PQM row matched the QUAL qualification name and qualification type.'
)]
matched[, ValidationResult := ifelse(HasPqmMatch & C2_TypeMatch & C3_MinTimeMatch & C4_WILMatch & C5_HeqfMatch & C6_SubsidyMatch, 'PASS', 'FAIL')]
matched[, Status := ValidationResult]

control_summary <- data.table(
  Control = c('C2', 'C3', 'C4', 'C5', 'C6'),
  PassCount = c(
    matched[, sum(C2_TypeMatch, na.rm = TRUE)],
    matched[, sum(C3_MinTimeMatch, na.rm = TRUE)],
    matched[, sum(C4_WILMatch, na.rm = TRUE)],
    matched[, sum(C5_HeqfMatch, na.rm = TRUE)],
    matched[, sum(C6_SubsidyMatch, na.rm = TRUE)]
  ),
  FailCount = c(
    matched[, sum(!C2_TypeMatch, na.rm = TRUE)],
    matched[, sum(!C3_MinTimeMatch, na.rm = TRUE)],
    matched[, sum(!C4_WILMatch, na.rm = TRUE)],
    matched[, sum(!C5_HeqfMatch, na.rm = TRUE)],
    matched[, sum(!C6_SubsidyMatch, na.rm = TRUE)]
  )
)
control_summary[, Status := ifelse(FailCount == 0, 'PASS', 'FAIL')]

cat('=== RULE 38 SUMMARY ===\n')
cat('Approved QUAL rows:', nrow(approved), '\n')
cat('PQM matches:', matched[, sum(HasPqmMatch)], '\n')
cat('Overall PASS:', matched[, sum(ValidationResult == 'PASS')], '\n')
cat('Overall FAIL:', matched[, sum(ValidationResult == 'FAIL')], '\n\n')

cat('=== CONTROL SUMMARY ===\n')
print(control_summary)

cat('\n=== FULL RESULT DETAIL ===\n')
print(matched[, .(
  QualCode,
  QualName,
  ApprovalStatus,
  QualType,
  MinTimeTotal,
  MinTimeWIL,
  HeqfIndicator,
  TotalSubsidy,
  PqmName,
  PqmQualType,
  PqmMinTimeTotal,
  PqmWIL,
  PqmAccreditation,
  PqmTotalSubsidy,
  MatchNote,
  FailedControls,
  ValidationResult
)])
";
    }
}
