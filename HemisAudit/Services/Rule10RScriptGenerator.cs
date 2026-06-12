using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule10RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule10ValidationRequest req)
    {
        var qual = RString(req.QualTable ?? "dbo_QUAL");
        var stud = RString(req.StudTable ?? "dbo_STUD");
        var creg = RString(req.CregTable ?? "dbo_CREG");
        var crse = RString(req.CrseTable ?? "dbo_CRSE");
        var qcol = RString(req.QualColumn ?? "_005");
        var scol = RString(req.StudColumn ?? "_007");

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

qual_table <- '{qual}'
stud_table <- '{stud}'
creg_table <- '{creg}'
crse_table <- '{crse}'
qual_col   <- '{qcol}'
stud_col   <- '{scol}'

qual <- copy(ds[[qual_table]]); safe_names(qual)
stud <- copy(ds[[stud_table]]); safe_names(stud)
creg <- copy(ds[[creg_table]]); safe_names(creg)
crse <- copy(ds[[crse_table]]); safe_names(crse)

sc <- gsub('^_', 'X', stud_col)
qc <- gsub('^_', 'X', qual_col)

force_char_trim(stud, c(sc, qc))
force_char_trim(creg, c(sc, qc))
force_char_trim(qual, c(qc))

valid_stud <- stud[!is.na(get(sc)) & get(sc) != '', .(KEY = norm(get(sc)))]
valid_qual <- qual[!is.na(get(qc)) & get(qc) != '', .(KEY = norm(get(qc)))]
setkey(valid_stud, KEY)
setkey(valid_qual, KEY)

creg[, KEY_STUD := norm(col_val(.SD, sc))]
ghost <- creg[is.na(KEY_STUD) | KEY_STUD == '' | !KEY_STUD %in% valid_stud$KEY,
              .(Check = 'Ghost Student', RefValue = KEY_STUD)]

creg[, KEY_QUAL := norm(col_val(.SD, qc))]
invalid_qual <- creg[!is.na(KEY_QUAL) & KEY_QUAL != '' & !KEY_QUAL %in% valid_qual$KEY,
                     .(Check = 'Invalid Qual Code', RefValue = KEY_QUAL)]

exceptions <- rbindlist(list(ghost, invalid_qual), use.names = TRUE, fill = TRUE)
exceptions[, Status := 'FAIL']

print_summary(exceptions, 'Rule 10: Integrity Exceptions')
if (nrow(exceptions) == 0) cat('No integrity exceptions found.\n') else print(exceptions)
";
    }
}
