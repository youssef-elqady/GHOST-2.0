# API database connection configuration

The API no longer ships with a database target. Every environment must explicitly provide its own SQL Server connection string.

## Required setting

Set `ConnectionStrings__DefaultConnection` in the API process environment or the environment's approved secret/configuration provider.

Example for a developer-owned local database:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost;Database=GHOST_DEV;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
```

The example is illustrative only. Confirm the intended server and catalog before using a real connection string. Do not copy a production connection string into a development shell.

## Startup validation

The API validates the connection string at startup without opening a database connection. It requires:
- A valid SQL Server connection string.
- An explicit server/data source.
- An explicit database catalog.
- No attached database file.

The API refuses these catalogs:
- `GHOST2` — explicitly excluded.
- `GHOST_AUTH02_IT` — reserved exclusively for the isolated AUTH-02 SQL integration test harness.
- SQL Server system catalogs (`master`, `model`, `msdb`, `tempdb`).

No connection string is committed to source control. There is no fallback to another database when the setting is absent or invalid.

## Safety boundary

This validation is a configuration guard, not approval to connect to a database. Before any DB-connected test or migration, verify the exact server/catalog and follow the project's backup/restore and migration gates. Never target production or `GHOST2`.
