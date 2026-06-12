using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule54RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule54ValidationRequest req)
    {
        var credTable      = RString(req.CredTable);
        var qualTable      = RString(req.QualTable);
        var pqmTable       = RString(req.PqmTable);
        var credIdCol      = RString(req.CredIdCol);
        var credResearch1  = RString(req.CredResearch1Col);
        var qualIdCol      = RString(req.QualIdCol);
        var qualNmCol      = RString(req.QualNameCol);
        var pqmNmCol       = RString(req.PqmNameCol);

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

cred_table    <- '{credTable}'
qual_table    <- '{qualTable}'
pqm_table     <- '{pqmTable}'
cred_id_col   <- '{credIdCol}'
cred_research1<- '{credResearch1}'
qual_id_col   <- '{qualIdCol}'
qual_nm_col   <- '{qualNmCol}'
pqm_nm_col    <- '{pqmNmCol}'

cred <- copy(ds[[cred_table]]); safe_names(cred)
qual <- copy(ds[[qual_table]]); safe_names(qual)
pqm  <- copy(ds[[pqm_table]]); safe_names(pqm)

cc <- gsub('^_', 'X', cred_id_col)
qc <- gsub('^_', 'X', qual_id_col)
qn <- gsub('^_', 'X', qual_nm_col)
rc <- gsub('^_', 'X', cred_research1)

force_char_trim(cred, c(cc, rc))
force_char_trim(qual, c(qc, qn))

qual_lookup <- qual[, .(QualKey = norm(col_val(.SD, qc)), QualName = col_val(.SD, qn))]
setkey(qual_lookup, QualKey)

cred[, CRED_QUAL     := norm(col_val(.SD, cc))]
cred[, CRED_RESEARCH := norm(col_val(.SD, rc))]
result <- merge(cred[, .(CRED_QUAL, CRED_RESEARCH)], qual_lookup, by.x = 'CRED_QUAL', by.y = 'QualKey', all.x = TRUE)
result[, Status := fcase(
  is.na(QualName) | QualName == '', 'FAIL - Qual not in QUAL table',
  default = 'PASS'
)]

exceptions <- result[Status != 'PASS']
print_summary(exceptions, 'Rule 54: CRED/QUAL/PQM')
if (nrow(exceptions) == 0) cat('No exceptions found.\n') else print(exceptions)
";
    }
}
