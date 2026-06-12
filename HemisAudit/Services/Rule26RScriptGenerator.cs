using HemisAudit.ViewModels;

namespace HemisAudit.Services;

public static class Rule26RScriptGenerator
{
    private static string RString(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Generate(Rule26ValidationRequest req)
    {
        var profTable         = RString(req.ProfTable);
        var payrollTable      = RString(req.PayrollTable);
        var profPersonnelCol  = RString(req.ProfPersonnelColumn);
        var profEmplTypeCol   = RString(req.ProfEmploymentTypeColumn);
        var profGenderCol     = RString(req.ProfGenderColumn);
        var profGroupCol      = RString(req.ProfGroupColumn);
        var profBirthCol      = RString(req.ProfBirthDateColumn);
        var payrollPersonnelCol = RString(req.PayrollPersonnelColumn);
        var payrollEmplTypeCol  = RString(req.PayrollEmploymentTypeColumn);
        var payrollGenderCol    = RString(req.PayrollGenderColumn);
        var payrollGroupCol     = RString(req.PayrollGroupColumn);
        var payrollBirthCol     = RString(req.PayrollBirthDateColumn);

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

prof_table    <- '{profTable}'
payroll_table <- '{payrollTable}'

prof    <- copy(ds[[prof_table]]);    safe_names(prof)
payroll <- copy(ds[[payroll_table]]); safe_names(payroll)

prof_personnel_col    <- gsub('^_', 'X', '{profPersonnelCol}')
prof_empl_type_col    <- gsub('^_', 'X', '{profEmplTypeCol}')
prof_gender_col       <- gsub('^_', 'X', '{profGenderCol}')
prof_group_col        <- gsub('^_', 'X', '{profGroupCol}')
prof_birth_col        <- gsub('^_', 'X', '{profBirthCol}')
payroll_personnel_col <- '{payrollPersonnelCol}'
payroll_empl_type_col <- '{payrollEmplTypeCol}'
payroll_gender_col    <- '{payrollGenderCol}'
payroll_group_col     <- '{payrollGroupCol}'
payroll_birth_col     <- '{payrollBirthCol}'

ProfBase <- prof[, .(
  PersonnelKey   = norm(col_val(.SD, prof_personnel_col)),
  EmploymentType = trimws(as.character(col_val(.SD, prof_empl_type_col))),
  GenderValue    = toupper(trimws(as.character(col_val(.SD, prof_gender_col)))),
  GroupValue     = trimws(as.character(col_val(.SD, prof_group_col))),
  BirthValue     = trimws(as.character(col_val(.SD, prof_birth_col)))
)]
ProfBase <- ProfBase[!is.na(PersonnelKey) & PersonnelKey != '']

PayrollBase <- payroll[, .(
  PersonnelKey   = norm(col_val(.SD, payroll_personnel_col)),
  EmploymentType = trimws(as.character(col_val(.SD, payroll_empl_type_col))),
  GenderValue    = toupper(trimws(as.character(col_val(.SD, payroll_gender_col)))),
  GroupValue     = trimws(as.character(col_val(.SD, payroll_group_col))),
  BirthValue     = trimws(as.character(col_val(.SD, payroll_birth_col)))
)]

setkey(ProfBase,    PersonnelKey)
setkey(PayrollBase, PersonnelKey)

c1 <- ProfBase[!PayrollBase, .(PersonnelKey, Control = 1L, ControlName = 'PROF without Payroll record',
                                PROF_Value = PersonnelKey, Payroll_Value = NA_character_)]
linked <- merge(ProfBase, PayrollBase, by = 'PersonnelKey', suffixes = c('_p', '_y'))
c2 <- linked[substr(EmploymentType_p, 1, 1) != substr(EmploymentType_y, 1, 1),
             .(PersonnelKey, Control = 2L, ControlName = 'Employment Type Mismatch',
               PROF_Value = EmploymentType_p, Payroll_Value = EmploymentType_y)]
c3 <- linked[substr(GenderValue_p, 1, 1) != substr(GenderValue_y, 1, 1),
             .(PersonnelKey, Control = 3L, ControlName = 'Gender Mismatch',
               PROF_Value = GenderValue_p, Payroll_Value = GenderValue_y)]
c4 <- linked[norm(GroupValue_p) != norm(GroupValue_y),
             .(PersonnelKey, Control = 4L, ControlName = 'Group Mismatch',
               PROF_Value = GroupValue_p, Payroll_Value = GroupValue_y)]
c5 <- linked[norm(BirthValue_p) != norm(BirthValue_y),
             .(PersonnelKey, Control = 5L, ControlName = 'Birth Date Mismatch',
               PROF_Value = BirthValue_p, Payroll_Value = BirthValue_y)]

exceptions <- rbindlist(list(c1, c2, c3, c4, c5), use.names = TRUE, fill = TRUE)
exceptions[, Status := 'FAIL']

cat('-- Rule 26: PROF to Payroll 5-Control Validation --\n')
cat(sprintf('PROF population    : %d\n', nrow(ProfBase)))
cat(sprintf('Payroll population : %d\n', nrow(PayrollBase)))
cat(sprintf('Total exceptions   : %d\n', nrow(exceptions)))
if (nrow(exceptions) > 0) print(exceptions) else cat('No exceptions found.\n')
";
    }
}
