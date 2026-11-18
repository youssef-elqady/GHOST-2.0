using System.Text;
using GHOST.Infrastructure.Identity;
using GHOST.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GHOST.Application.Identity.Bootstrap;

if (Console.IsInputRedirected || Console.IsOutputRedirected)
{
    Console.Error.WriteLine("Bootstrap requires an interactive terminal. No changes were made.");
    return 2;
}

var connectionString = Environment.GetEnvironmentVariable("GHOST_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Set GHOST_CONNECTION_STRING to the intended SQL Server database. No changes were made.");
    return 2;
}

SqlConnectionStringBuilder sqlConnection;
try
{
    sqlConnection = new SqlConnectionStringBuilder(connectionString);
}
catch
{
    Console.Error.WriteLine("GHOST_CONNECTION_STRING is not a valid SQL Server connection string. No changes were made.");
    return 2;
}

if (string.IsNullOrWhiteSpace(sqlConnection.InitialCatalog))
{
    Console.Error.WriteLine("The connection string must explicitly name its target database. No changes were made.");
    return 2;
}

string userName;
string fullName;
string password;
string passwordConfirmation;

try
{
    Console.WriteLine("GHOST — one-time first Admin bootstrap");
    Console.WriteLine($"Target SQL Server: {sqlConnection.DataSource}");
    Console.WriteLine($"Target database: {sqlConnection.InitialCatalog}");
    Console.WriteLine("This command does not run migrations. The AuditLogs schema must already exist.");
    Console.Write($"Type BOOTSTRAP {sqlConnection.InitialCatalog} to confirm this exact target: ");
    if (!string.Equals(Console.ReadLine(), $"BOOTSTRAP {sqlConnection.InitialCatalog}", StringComparison.Ordinal))
    {
        Console.WriteLine("Confirmation not provided. No changes were made.");
        return 2;
    }

    Console.Write("Admin username: ");
    userName = (Console.ReadLine() ?? string.Empty).Trim();
    Console.Write("Admin full name: ");
    fullName = (Console.ReadLine() ?? string.Empty).Trim();

    if (string.IsNullOrWhiteSpace(userName) || userName.Length > 256 ||
        string.IsNullOrWhiteSpace(fullName) || fullName.Length > 150)
    {
        Console.Error.WriteLine("Username is required (max 256 characters) and full name is required (max 150 characters). No changes were made.");
        return 2;
    }

    Console.Write("Password (input hidden): ");
    password = ReadSecret();
    Console.Write("Confirm password (input hidden): ");
    passwordConfirmation = ReadSecret();

    if (!string.Equals(password, passwordConfirmation, StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Passwords do not match. No changes were made.");
        return 2;
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Input cancelled. No changes were made.");
    return 2;
}

var services = new ServiceCollection();

services.AddLogging();
services.AddDbContext<GHOSTDbContext>(options => options.UseSqlServer(connectionString));

services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequiredLength = 12;
        options.Password.RequiredUniqueChars = 4;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<GHOSTDbContext>();

services.AddScoped<IAdminBootstrapService, AdminBootstrapService>();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();

try
{
    var bootstrapService = scope.ServiceProvider.GetRequiredService<IAdminBootstrapService>();
    var result = await bootstrapService.BootstrapAsync(
        new AdminBootstrapRequest(userName, fullName, password));

    switch (result.Status)
    {
        case AdminBootstrapStatus.Succeeded:
            Console.WriteLine($"First Admin created successfully: {userName} ({result.UserId:D}).");
            Console.WriteLine("The account is active and is flagged to require a password change; sign-in enforcement will be implemented in the authentication flow.");
            Console.WriteLine("No database migration was executed.");
            return 0;

        case AdminBootstrapStatus.AdminAlreadyExists:
            Console.Error.WriteLine("An Admin already exists. Bootstrap is one-time and has been refused.");
            return 3;

        case AdminBootstrapStatus.IdentityRejected:
            var codes = result.ErrorCodes is { Count: > 0 }
                ? string.Join(", ", result.ErrorCodes)
                : "No error code was returned";
            Console.Error.WriteLine($"Identity rejected the bootstrap operation. Error codes: {codes}.");
            return 1;

        case AdminBootstrapStatus.RoleConflict:
            Console.Error.WriteLine("A conflicting role record exists. Bootstrap was rolled back.");
            return 1;

        default:
            Console.Error.WriteLine("Bootstrap returned an unsupported result. No success was reported.");
            return 1;
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Bootstrap failed; the transaction was rolled back. Error type: {exception.GetType().Name}.");
    Console.Error.WriteLine("Review the server-side configuration and database state before retrying.");
    return 1;
}
finally
{
    // Drop references; immutable .NET strings cannot be reliably zeroed.
    password = string.Empty;
    passwordConfirmation = string.Empty;
}

static string ReadSecret()
{
    var buffer = new StringBuilder();

    while (true)
    {
        var key = Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return buffer.ToString();
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (buffer.Length > 0)
                buffer.Length--;

            continue;
        }

        if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            throw new OperationCanceledException();

        if (!char.IsControl(key.KeyChar))
            buffer.Append(key.KeyChar);
    }
}
