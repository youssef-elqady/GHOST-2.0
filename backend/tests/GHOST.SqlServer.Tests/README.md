# GHOST SQL Server integration tests

This project contains opt-in SQL Server integration tests for AUTH-02. It is deliberately separate from the ordinary unit-test project.

## Safety boundary

- The only accepted connection source is the environment variable `GHOST_AUTH02_TEST_CONNECTION_STRING`.
- The parsed SQL Server `Initial Catalog` must equal `GHOST_AUTH02_IT` exactly (ordinal/case-sensitive comparison).
- Missing connection strings cause SQL integration tests to be skipped. The connection-guard unit tests still run.
- A supplied but invalid/unsafe connection string fails the tests; it does not silently skip.
- The test project never reads appsettings, `GHOST_CONNECTION_STRING`, or any application configuration.
- Tests do not create, drop, migrate, reset, truncate, or clean a database.
- The database must already exist and have the required migrations applied by a separately reviewed and approved operation.
- Append-only trigger tests insert a uniquely identified row inside a transaction and roll the transaction back. They do not issue cleanup DELETE statements.

## Run the always-safe connection guard tests

```powershell
dotnet test backend/tests/GHOST.SqlServer.Tests/GHOST.SqlServer.Tests.csproj --configuration Release
```

With no AUTH-02 connection variable configured, SQL Server tests are skipped while connection-guard unit tests execute.

## Run SQL Server integration tests

Only after verifying that the connection points to the dedicated `GHOST_AUTH02_IT` database:

```powershell
$env:GHOST_AUTH02_TEST_CONNECTION_STRING = "Server=YOUR_SQL_SERVER;Database=GHOST_AUTH02_IT;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
dotnet test backend/tests/GHOST.SqlServer.Tests/GHOST.SqlServer.Tests.csproj --configuration Release
Remove-Item Env:GHOST_AUTH02_TEST_CONNECTION_STRING
```

Use your own approved authentication method. Never commit a real connection string or paste credentials into chat, source files, terminal transcripts, or logs.

The test connection should use a least-privilege SQL principal. These tests require only access to read the database identity/schema and to insert/attempt mutation of an AuditLogs row within a transaction that is rolled back.
