# HEMIS System

ASP.NET Core MVC application for HEMIS validation workflows.

## Structure

The project is organised by responsibility:

- `Controllers/`: MVC controllers and route entry points.
- `Data/`: EF Core context, bootstrap, and database seeding.
- `Filters/`: MVC request/action filters such as password-age enforcement.
- `Helpers/`: small reusable helper and policy classes.
- `Models/`: domain and persistence models.
- `Services/`: business logic, exports, email, and rule services.
- `ViewModels/`: models used by views and controller/view binding.
- `Views/`: Razor views grouped by controller.
- `wwwroot/`: static assets and runtime upload directories.
- `Scripts/`: local helper scripts and SQL diagnostics.
- `Docs/Images/`: documentation and README image assets.

## Script Layout

- `Scripts/run system.ps1`: builds and launches the app locally.
- `Scripts/run system.cmd`: Windows wrapper for the PowerShell launcher.
- `Scripts/Sql/Rule29/`: saved diagnostic SQL files for Rule 29 investigation.

The launcher now writes temporary runtime output to `.run/` instead of cluttering the repository root.

## Repository Hygiene

The repository should not track generated or user-local artifacts such as:

- build verification folders
- process id files
- runtime logs
- uploaded profile/message files

Those paths are ignored in `.gitignore` so the source tree stays focused on application code.
