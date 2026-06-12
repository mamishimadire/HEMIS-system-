namespace HemisAudit.Services;

internal static class RScriptScaffold
{
    public const string DefaultDataDirectory = @"C:/Users/Mamishi.Madire/Downloads/2026 hemis data";

    public static string BuildDataLoadingPrelude() => $@"options(timeout = max(300, getOption('timeout')))

.hemis_install_package <- function(pkg) {{
  if (requireNamespace(pkg, quietly = TRUE)) return(TRUE)

  message(sprintf('Installing %s ...', pkg))
  repos <- c(
    'https://cloud.r-project.org',
    'https://cran.r-project.org',
    'https://cran.rstudio.com'
  )

  for (repo in repos) {{
    tryCatch({{
      suppressWarnings(install.packages(pkg, repos = repo, quiet = TRUE))
    }}, error = function(e) NULL, warning = function(w) NULL)

    if (requireNamespace(pkg, quietly = TRUE)) {{
      return(TRUE)
    }}
  }}

  if (.Platform$OS.type == 'windows' && getRversion() < '4.0.0') {{
    stop(paste(
      sprintf('Automatic installation of %s failed.', pkg),
      sprintf('This machine is using R %s, and current CRAN Windows binaries are no longer provided for that R series.', getRversion()),
      'Upgrade R to version 4.x and rerun the script.',
      sprintf('If you cannot upgrade R yet, ask IT to install %s manually before running this script.', pkg),
      sep = '\n'
    ))
  }}

  tryCatch({{
    suppressWarnings(install.packages(pkg, repos = 'https://cloud.r-project.org', quiet = TRUE, type = 'source'))
  }}, error = function(e) NULL, warning = function(w) NULL)

  if (!requireNamespace(pkg, quietly = TRUE)) {{
    stop(paste(
      sprintf('The %s package could not be installed automatically.', pkg),
      'Install it manually and rerun the script:',
      sprintf('install.packages(""%s"", repos = ""https://cloud.r-project.org"")', pkg),
      sep = '\n'
    ))
  }}

  TRUE
}}

.hemis_install_packages <- function(pkgs) {{
  for (pkg in pkgs) .hemis_install_package(pkg)
  invisible(TRUE)
}}

.hemis_install_packages(c('data.table', 'openxlsx'))
suppressPackageStartupMessages(library(data.table))
suppressPackageStartupMessages(library(openxlsx))

# Update this folder if your CSV files are stored somewhere else.
default_data_dir <- '{DefaultDataDirectory}'

csv_path_for_table <- function(table_name) {{
  table_name <- trimws(as.character(table_name)[1])
  file.path(default_data_dir, paste0(gsub('\\.', '_', table_name), '.csv'))
}}

load_table_csv <- function(table_name) {{
  csv_path <- csv_path_for_table(table_name)
  if (!file.exists(csv_path)) {{
    stop(sprintf('CSV file not found for table %s at %s', table_name, csv_path))
  }}

  fread(csv_path, na.strings = c('', 'NA'), showProgress = FALSE)
}}

table_exists <- function(table_name) {{
  if (exists('ds', inherits = FALSE) && !is.null(ds) && !inherits(ds, 'hemi_ds')) {{
    return(table_name %in% names(ds))
  }}

  file.exists(csv_path_for_table(table_name))
}}

.hemi_ds_cache <- new.env(parent = emptyenv())

`[[.hemi_ds` <- function(x, i, ...) {{
  table_name <- trimws(as.character(i)[1])
  if (!nzchar(table_name)) stop('Table name is required.')

  if (!exists(table_name, envir = .hemi_ds_cache, inherits = FALSE)) {{
    assign(table_name, load_table_csv(table_name), envir = .hemi_ds_cache)
  }}

  get(table_name, envir = .hemi_ds_cache, inherits = FALSE)
}}

if (!exists('ds', inherits = FALSE) || is.null(ds)) {{
  ds <- structure(list(), class = 'hemi_ds')
}}

.hemi_current_script_path <- function() {{
  args <- commandArgs(trailingOnly = FALSE)
  file_arg <- grep('^--file=', args, value = TRUE)
  if (length(file_arg) > 0) {{
    return(normalizePath(sub('^--file=', '', file_arg[1]), winslash = '/', mustWork = FALSE))
  }}

  NA_character_
}}

.hemi_safe_file_stem <- function(x) {{
  x <- trimws(as.character(x)[1])
  x <- gsub('\\.[Rr]$', '', basename(x))
  x <- gsub('_R_Script$', '', x, ignore.case = TRUE)
  x <- gsub('[^A-Za-z0-9]+', '_', x)
  x <- gsub('^_+|_+$', '', x)
  if (!nzchar(x)) x <- 'HEMIS_R_Results'
  x
}}

.hemi_default_output_file <- function(rule_label = 'HEMIS_R_Results') {{
  if (exists('output_file', inherits = FALSE)) {{
    configured <- trimws(as.character(get('output_file', inherits = FALSE))[1])
    if (nzchar(configured)) return(configured)
  }}

  script_path <- .hemi_current_script_path()
  stem <- if (!is.na(script_path) && nzchar(script_path)) {{
    .hemi_safe_file_stem(script_path)
  }} else {{
    .hemi_safe_file_stem(rule_label)
  }}

  file.path(default_data_dir, paste0(stem, '_Results.xlsx'))
}}

.hemi_sheet_name <- function(name, used_names = character()) {{
  label <- gsub('[\\[\\]\\:\\*\\?/\\\\]', '_', trimws(as.character(name)[1]))
  label <- gsub('_+', ' ', label)
  label <- trimws(label)
  if (!nzchar(label)) label <- 'Sheet'
  label <- substr(label, 1, 31)

  if (!(label %in% used_names)) return(label)

  idx <- 2L
  repeat {{
    suffix <- paste0(' ', idx)
    candidate <- paste0(substr(label, 1, max(1, 31 - nchar(suffix))), suffix)
    if (!(candidate %in% used_names)) return(candidate)
    idx <- idx + 1L
  }}
}}

.hemi_is_result_table <- function(obj_name, obj) {{
  if (!(is.data.frame(obj) || data.table::is.data.table(obj))) return(FALSE)

  preferred_names <- c(
    'control_summary', 'validated', 'matched', 'result', 'results',
    'exceptions', 'review_rows', 'pass_rows', 'fail_rows',
    'matched_rows', 'summary_table', 'summary_dt'
  )

  if (tolower(obj_name) %in% preferred_names) return(TRUE)

  cols <- toupper(names(obj))
  any(cols %in% c(
    'STATUS', 'VALIDATIONRESULT', 'EXCEPTIONREASON', 'FAILEDCONTROLS',
    'REASON', 'MATCHNOTE', 'CONTROL'
  ))
}}

.hemi_collect_export_tables <- function(env = .GlobalEnv) {{
  object_names <- ls(envir = env, all.names = FALSE)
  preferred_order <- c(
    'control_summary', 'validated', 'matched', 'result', 'results',
    'exceptions', 'review_rows', 'pass_rows', 'fail_rows',
    'matched_rows', 'summary_table', 'summary_dt'
  )

  collected <- list()
  used_sheets <- character()

  add_object <- function(obj_name) {{
    if (!(obj_name %in% object_names)) return()
    obj <- get(obj_name, envir = env, inherits = FALSE)
    if (!.hemi_is_result_table(obj_name, obj)) return()
    if (length(obj) == 0 && !nrow(as.data.table(obj))) return()

    sheet_name <- .hemi_sheet_name(obj_name, used_sheets)
    used_sheets <<- c(used_sheets, sheet_name)
    collected[[sheet_name]] <<- as.data.table(obj)
  }}

  for (name in preferred_order) add_object(name)

  remaining <- setdiff(object_names, preferred_order)
  for (name in remaining) add_object(name)

  collected
}}

.hemi_write_results_excel <- function(rule_label = 'HEMIS_R_Results') {{
  sheets <- .hemi_collect_export_tables(.GlobalEnv)
  if (length(sheets) == 0) {{
    cat(sprintf('\nNo result tables were detected for %s. Excel export skipped.\n', rule_label))
    return(invisible(NA_character_))
  }}

  output_path <- .hemi_default_output_file(rule_label)
  dir.create(dirname(output_path), recursive = TRUE, showWarnings = FALSE)

  wb <- createWorkbook()
  for (sheet_name in names(sheets)) {{
    addWorksheet(wb, sheet_name)
    writeDataTable(wb, sheet = sheet_name, x = as.data.frame(sheets[[sheet_name]]), withFilter = TRUE)
    setColWidths(wb, sheet = sheet_name, cols = 1:ncol(sheets[[sheet_name]]), widths = 'auto')
  }}

  saveWorkbook(wb, output_path, overwrite = TRUE)
  cat(sprintf('\nExcel output saved: %s\n', normalizePath(output_path, winslash = '/', mustWork = FALSE)))
  invisible(output_path)
}}

";

    public static string BuildAutoExportFooter(string ruleLabel)
    {
        var safeLabel = (ruleLabel ?? "HEMIS_R_Results")
            .Replace("\\", "\\\\")
            .Replace("'", "\\'");

        return $@"

.hemi_write_results_excel('{safeLabel}')
";
    }
}
