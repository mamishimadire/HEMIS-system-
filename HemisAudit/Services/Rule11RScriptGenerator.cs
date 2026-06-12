using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule11RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule11ValidationRequest req)
    {
        var cesmTable          = RString(req.CesmTable);
        var cesmIdCol          = RString(req.CesmIdCol);
        var cesmCodeCol        = RString(req.CesmCodeCol);
        var qualTable          = RString(req.QualTable);
        var qualIdCol          = RString(req.QualIdCol);
        var qualApprovalCol    = RString(req.QualApprovalCol);
        var qualApprovalVal    = RString(req.QualApprovalFilterValue);
        var qualHeqfCol        = RString(req.QualHeqfTypeCol);
        var qualTypeCodesText  = RString(req.QualTypeCodesText);
        var pqmTable           = RString(req.PqmTable);
        var pqmCodeCol         = RString(req.PqmCodeCol);
        var pqmHeqfCol         = RString(req.PqmHeqfTypeCol);

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

cesm_table           <- '{cesmTable}'
cesm_id_col          <- '{cesmIdCol}'
cesm_code_col        <- '{cesmCodeCol}'
qual_table           <- '{qualTable}'
qual_id_col          <- '{qualIdCol}'
qual_approval_col    <- '{qualApprovalCol}'
qual_approval_val    <- '{qualApprovalVal}'
qual_heqf_col        <- '{qualHeqfCol}'
qual_type_codes_text <- '{qualTypeCodesText}'
pqm_table            <- '{pqmTable}'
pqm_code_col         <- '{pqmCodeCol}'
pqm_heqf_col         <- '{pqmHeqfCol}'

type_codes <- trimws(unlist(strsplit(qual_type_codes_text, ',')))

cesm <- copy(ds[[cesm_table]]); safe_names(cesm)
qual <- copy(ds[[qual_table]]); safe_names(qual)
pqm  <- copy(ds[[pqm_table]]); safe_names(pqm)

cesm_ic <- gsub('^_', 'X', cesm_id_col)
cesm_cc <- gsub('^_', 'X', cesm_code_col)
qual_ic  <- gsub('^_', 'X', qual_id_col)
qual_ac  <- gsub('^_', 'X', qual_approval_col)
qual_hc  <- gsub('^_', 'X', qual_heqf_col)
pqm_cc   <- gsub('^_', 'X', pqm_code_col)
pqm_hc   <- gsub('^_', 'X', pqm_heqf_col)

force_char_trim(cesm, c(cesm_ic, cesm_cc))
force_char_trim(qual, c(qual_ic, qual_ac, qual_hc))
force_char_trim(pqm,  c(pqm_cc, pqm_hc))

qual[, QUAL_KEY  := norm(col_val(.SD, qual_ic))]
qual[, APPROVAL  := norm(col_val(.SD, qual_ac))]
qual[, HEQF_TYPE := norm(col_val(.SD, qual_hc))]
cesm[, CESM_KEY  := norm(col_val(.SD, cesm_ic))]
cesm[, CESM_CD   := norm(col_val(.SD, cesm_cc))]
pqm[,  PQM_CD    := norm(col_val(.SD, pqm_cc))]

approved_keys <- qual[APPROVAL == norm(qual_approval_val), QUAL_KEY]

missing_in_qual <- cesm[!CESM_KEY %in% qual$QUAL_KEY,
                         .(QualCode = CESM_KEY, Reason = 'QUAL code not found in QUAL table')]

not_approved <- cesm[CESM_KEY %in% qual$QUAL_KEY & !CESM_KEY %in% approved_keys,
                      .(QualCode = CESM_KEY, Reason = paste0('Not approved (', qual_approval_col, ' != ', qual_approval_val, ')'))]

pg_mismatch <- qual[HEQF_TYPE != '' & !HEQF_TYPE %in% type_codes & QUAL_KEY %in% cesm$CESM_KEY,
                     .(QualCode = QUAL_KEY, Reason = paste0('HEQF type (', HEQF_TYPE, ') not in expected list'))]

pqm_codes <- unique(pqm$PQM_CD)
missing_pqm <- cesm[!CESM_CD %in% pqm_codes,
                     .(QualCode = CESM_KEY, Reason = paste0('CESM code (', CESM_CD, ') not in PQM table'))]

exceptions <- rbindlist(list(missing_in_qual, not_approved, pg_mismatch, missing_pqm),
                         use.names = TRUE, fill = TRUE)
exceptions[, Status := 'FAIL']

print_summary(exceptions, 'Rule 11: QUAL/CESM/PQM Exceptions')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }
}
