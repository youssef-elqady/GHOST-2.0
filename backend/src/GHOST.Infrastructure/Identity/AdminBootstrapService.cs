using System.Data;
using System.Data.Common;
using System.Text.Json;
using GHOST.Application.Identity.Bootstrap;
using GHOST.Domain.Entities;
using GHOST.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GHOST.Infrastructure.Identity;

public sealed class AdminBootstrapService : IAdminBootstrapService
{
    private const string LockResource = "GHOST:AUTH:FirstAdminBootstrap";

    private readonly GHOSTDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;

    public AdminBootstrapService(
        GHOSTDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<AdminBootstrapResult> BootstrapAsync(
        AdminBootstrapRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var lockResult = await AcquireBootstrapLockAsync(
            _db,
            transaction.GetDbTransaction(),
            cancellationToken);

        if (lockResult < 0)
        {
            throw new InvalidOperationException(
                $"Bootstrap application lock failed with SQL return code {lockResult}.");
        }

        var existingAdminRole = await _roleManager.FindByNameAsync("Admin");
        if (existingAdminRole is not null)
        {
            var existingAdmins = await _userManager.GetUsersInRoleAsync("Admin");
            if (existingAdmins.Count > 0)
            {
                return new AdminBootstrapResult(AdminBootstrapStatus.AdminAlreadyExists);
            }
        }

        var adminRoleStatus = await EnsureRoleAsync("Admin");
        if (adminRoleStatus is not null)
            return adminRoleStatus;

        var staffRoleStatus = await EnsureRoleAsync("Staff");
        if (staffRoleStatus is not null)
            return staffRoleStatus;

        var admin = new ApplicationUser
        {
            UserName = request.UserName,
            FullName = request.FullName,
            IsActive = true,
            MustChangePassword = true,
            EmailConfirmed = false
        };

        var createUserResult = await _userManager.CreateAsync(admin, request.Password);
        if (!createUserResult.Succeeded)
        {
            return new AdminBootstrapResult(
                AdminBootstrapStatus.IdentityRejected,
                ErrorCodes: createUserResult.Errors
                    .Select(error => error.Code)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray());
        }

        var addRoleResult = await _userManager.AddToRoleAsync(admin, "Admin");
        if (!addRoleResult.Succeeded)
        {
            return new AdminBootstrapResult(
                AdminBootstrapStatus.IdentityRejected,
                ErrorCodes: addRoleResult.Errors
                    .Select(error => error.Code)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray());
        }

        var auditPayload = JsonSerializer.Serialize(new
        {
            admin.UserName,
            admin.FullName,
            Roles = new[] { "Admin" },
            admin.MustChangePassword
        });

        _db.AuditLogs.Add(AuditLog.Create(
            actorType: "System",
            userId: null,
            action: "Admin.Bootstrapped",
            entityType: "User",
            entityId: admin.Id.ToString("D"),
            newValues: auditPayload));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AdminBootstrapResult(
            AdminBootstrapStatus.Succeeded,
            UserId: admin.Id);
    }

    private async Task<AdminBootstrapResult?> EnsureRoleAsync(string roleName)
    {
        var existingRole = await _roleManager.FindByNameAsync(roleName);
        if (existingRole is not null)
        {
            return string.Equals(existingRole.Name, roleName, StringComparison.Ordinal)
                ? null
                : new AdminBootstrapResult(AdminBootstrapStatus.RoleConflict);
        }

        var role = new IdentityRole<Guid>
        {
            Id = Guid.NewGuid(),
            Name = roleName
        };

        var result = await _roleManager.CreateAsync(role);
        if (result.Succeeded)
            return null;

        return new AdminBootstrapResult(
            AdminBootstrapStatus.IdentityRejected,
            ErrorCodes: result.Errors
                .Select(error => error.Code)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private static async Task<int> AcquireBootstrapLockAsync(
        GHOSTDbContext db,
        DbTransaction transaction,
        CancellationToken cancellationToken)
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

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
