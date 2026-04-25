# HEMIS Audit Management System

ASP.NET Core 8 MVC application for Rule 36 deceased-students validation.

Current MVC application database: SQLite file `HemisAuditDb.db`.

System database bootstrap: SQL Server LocalDB database `HEMISBaseSystem` is created automatically on startup with the core system tables from the HEMIS spec.

## Quick Start

1. Run the app:

```bash
dotnet run --project HemisAudit.csproj
```

2. Open:

```text
http://localhost:5080
```

3. Login with the seeded admin account:

| Field | Value |
| --- | --- |
| Email | Mamishi.Madire@sng.gt.com |
| Password | Admin@123! |

## What the system does

- Authenticates users with ASP.NET Identity
- Supports Admin, Director, Manager, DataAnalyst, and Trainee roles
- Lets Admins manage users and engagements
- Runs Rule 36 cross-table validation against a HEMIS SQL Server database
- Saves validation runs and audit logs in the application database
- Generates Excel, CSV, and SQL exports
- Writes export files to the current Windows user's Desktop on the server machine

## Rule 36 flow

1. Enter the HEMIS server name
2. Pick the database
3. Auto-detect tables
4. Auto-select matching columns
5. Verify the table counts
6. Run validation
7. Review validation rows, exceptions, charts, and SQL
8. Download Excel, CSV, and SQL files

## Notes

- The current MVC app still uses SQLite for its legacy screens, but startup now also creates the SQL Server `HEMISBaseSystem` database for the system tables.
- Rule 36 connections still use `Encrypt=False` to avoid client-side encryption issues in local environments.
- If a download button is clicked, the file is returned to the browser and also written to the Desktop.
