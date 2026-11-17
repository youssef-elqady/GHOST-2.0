# GHOST Bootstrap CLI

This is a dedicated, interactive, one-time first-Admin provisioning tool.

## Safety

- Requires an interactive terminal; redirected input/output is rejected.
- Requires the SQL Server connection string in `GHOST_CONNECTION_STRING`.
- Does not read the API's development connection string and does not run EF migrations.
- Requires the `AuditLogs` table and append-only trigger to have been deployed separately.
- Prompts for the exact confirmation word `BOOTSTRAP`.
- Reads the password without echoing it; the password is never accepted through arguments or environment variables.
- Creates only the `Admin` and `Staff` roles and one Admin user.
- Serializes attempts using a transaction-owned SQL Server application lock.
- Creates the user, role membership, and `Admin.Bootstrapped` audit record in one EF transaction.
- Sets `MustChangePassword = true`.

## Run

From the repository root in PowerShell, set a connection string for the intended database:

```powershell
$env:GHOST_CONNECTION_STRING = "Server=localhost;Database=<INTENDED_DATABASE>;Trusted_Connection=True;TrustServerCertificate=True"
dotnet run --project .\backend\src\GHOST.Bootstrap\GHOST.Bootstrap.csproj
```

Do not point this tool at the live `GHOST2` database until the migration, CLI, and SQL Server integration tests have been reviewed and the operator has separately approved that operation.

## Exit codes

- `0`: first Admin created and transaction committed.
- `1`: provisioning failed; transaction is rolled back.
- `2`: unsafe/non-interactive invocation, missing configuration, invalid input, or confirmation/password mismatch.
- `3`: an Admin already exists; no second Admin is created.
