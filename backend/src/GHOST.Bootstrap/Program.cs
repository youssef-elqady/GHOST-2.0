using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using GHOST.Domain.Entities;
using GHOST.Infrastructure.Identity;
using GHOST.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

const string LockResource = "GHOST:AUTH:FirstAdminBootstrap";

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

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();

var db = scope.ServiceProvider.GetRequiredService<GHOSTDbContext>();
var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
var normalizer = scope.ServiceProvider.GetRequiredService<ILookupNormalizer>();

try
{
    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    var lockResult = await AcquireBootstrapLockAsync(db, transaction.GetDbTransaction());

    if (lockResult < 0)
    {
        throw new InvalidOperationException($"Could not acquire the bootstrap application lock (SQL return code {lockResult}).");
    }

    var existingAdminRole = await roleManager.FindByNameAsync("Admin");
    if (existingAdminRole is not null)
    {
        var existingAdmins = await userManager.GetUsersInRoleAsync("Admin");
        if (existingAdmins.Count > 0)
        {
            await transaction.RollbackAsync();
            Console.Error.WriteLine("An Admin already exists. Bootstrap is one-time and has been refused.");
            return 3;
        }
    }

    await EnsureRoleAsync(roleManager, normalizer, "Admin");
    await EnsureRoleAsync(roleManager, normalizer, "Staff");

    var admin = new ApplicationUser
    {
        UserName = userName,
        FullName = fullName,
        IsActive = true,
        MustChangePassword = true,
        CreatedAt = DateTimeOffset.UtcNow,
        EmailConfirmed = false
    };

    var createUserResult = await userManager.CreateAsync(admin, password);
    if (!createUserResult.Succeeded)
    {
        throw new InvalidOperationException(
            "Identity rejected the Admin account: " +
            string.Join("; ", createUserResult.Errors.Select(error => $"{error.Code}: {error.Description}")));
    }

    var addRoleResult = await userManager.AddToRoleAsync(admin, "Admin");
    if (!addRoleResult.Succeeded)
    {
        throw new InvalidOperationException(
            "Identity could not assign the Admin role: " +
            string.Join("; ", addRoleResult.Errors.Select(error => $"{error.Code}: {error.Description}")));
    }

    var auditPayload = JsonSerializer.Serialize(new
    {
        admin.UserName,
        admin.FullName,
        Roles = new[] { "Admin" },
        admin.MustChangePassword
    });

    db.AuditLogs.Add(AuditLog.Create(
        actorType: "System",
        userId: null,
        action: "Admin.Bootstrapped",
        entityType: "User",
        entityId: admin.Id.ToString("D"),
        newValues: auditPayload));

    await db.SaveChangesAsync();
    await transaction.CommitAsync();

    Console.WriteLine($"First Admin created successfully: {admin.UserName} ({admin.Id:D}).");
    Console.WriteLine("The account is active and is flagged to require a password change; sign-in enforcement will be implemented in the authentication flow.");
    Console.WriteLine("No database migration was executed.");
    return 0;
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

static async Task<int> AcquireBootstrapLockAsync(
    GHOSTDbContext db,
    DbTransaction transaction)
{
    var connection = db.Database.GetDbConnection();
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = """
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock
            @Resource = @resource,
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 10000,
            @DbPrincipal = N'public';
        SELECT @result;
        """;

    var parameter = command.CreateParameter();
    parameter.ParameterName = "@resource";
    parameter.DbType = DbType.String;
    parameter.Size = 255;
    parameter.Value = LockResource;
    command.Parameters.Add(parameter);

    var result = await command.ExecuteScalarAsync();
    return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
}

static async Task EnsureRoleAsync(
    RoleManager<IdentityRole<Guid>> roleManager,
    ILookupNormalizer normalizer,
    string roleName)
{
    var existingRole = await roleManager.FindByNameAsync(roleName);
    if (existingRole is not null)
    {
        if (!string.Equals(existingRole.Name, roleName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"A conflicting role record exists for {roleName}.");
        }

        return;
    }

    var role = new IdentityRole<Guid>
    {
        Id = Guid.NewGuid(),
        Name = roleName,
        NormalizedName = normalizer.NormalizeName(roleName)
    };

    var result = await roleManager.CreateAsync(role);
    if (!result.Succeeded)
    {
        throw new InvalidOperationException(
            $"Could not create role {roleName}: " +
            string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));
    }
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
