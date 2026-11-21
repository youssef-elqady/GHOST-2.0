using System.Reflection;
using System.Security.Claims;
using GHOST.API.Authentication;
using GHOST.API.Controllers;
using GHOST.API.Security;
using GHOST.Application.Abstractions.CurrentUser;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GHOST.Tests.Security;

public sealed class AuthorizationTests
{
    private static ClaimsPrincipal Principal(Guid id, string? role, string tokenUse = "access")
    {
        var claims = new List<Claim>
        {
            new("sub", id.ToString("D")),
            new("token_use", tokenUse)
        };
        if (role is not null) claims.Add(new Claim("role", role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer", "unique_name", "role"));
    }

    private static IAuthorizationService CreateAuthorizationService()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddGhostJwtAuthentication(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Jwt:Issuer"] = "i",
                ["Authentication:Jwt:Audience"] = "a",
                ["Authentication:Jwt:SigningKey"] = new string('k', 40),
                ["Authentication:Jwt:AccessTokenLifetimeMinutes"] = "10"
            }).Build());
        return builder.Services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    [Theory]
    [InlineData("Admin", AuthorizationPolicies.AdminOnly, true)]
    [InlineData("Staff", AuthorizationPolicies.AdminOnly, false)]
    [InlineData("Admin", AuthorizationPolicies.StaffOrAdmin, true)]
    [InlineData("Staff", AuthorizationPolicies.StaffOrAdmin, true)]
    [InlineData(null, AuthorizationPolicies.StaffOrAdmin, false)]
    [InlineData("Other", AuthorizationPolicies.StaffOrAdmin, false)]
    public async Task Policies_EnforceRoleBoundaries(string? role, string policy, bool expected)
    {
        var result = await CreateAuthorizationService().AuthorizeAsync(
            Principal(Guid.NewGuid(), role), null, policy);
        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task PasswordChangeTokenCannotUseOperationalPolicies()
    {
        var service = CreateAuthorizationService();
        var principal = Principal(Guid.NewGuid(), "Admin", "password_change");

        Assert.False((await service.AuthorizeAsync(principal, null, AuthorizationPolicies.AdminOnly)).Succeeded);
        Assert.False((await service.AuthorizeAsync(principal, null, AuthorizationPolicies.StaffOrAdmin)).Succeeded);
    }

    [Fact]
    public async Task AnonymousUserIsDenied()
    {
        var result = await CreateAuthorizationService().AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()), null, AuthorizationPolicies.StaffOrAdmin);
        Assert.False(result.Succeeded);
    }

    /// <summary>Endpoint inventory: nothing is reachable anonymously except the auth entry points.</summary>
    [Fact]
    public void EveryControllerActionRequiresAuthorizationExceptAuthEntryPoints()
    {
        var allowedAnonymous = new HashSet<string> { "Login", "Refresh", "Logout" };
        var controllers = typeof(AuthController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract);

        foreach (var controller in controllers)
        {
            var classProtected = controller.GetCustomAttributes<AuthorizeAttribute>(true).Any();
            var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any());

            foreach (var action in actions)
            {
                var anonymous = action.GetCustomAttributes<AllowAnonymousAttribute>().Any();
                if (anonymous)
                {
                    Assert.True(
                        controller == typeof(AuthController) && allowedAnonymous.Contains(action.Name),
                        $"{controller.Name}.{action.Name} must not be anonymous.");
                    continue;
                }

                // No class attribute means the deny-by-default fallback policy (Admin) applies.
                // Require it to be explicit so intent stays reviewable.
                Assert.True(
                    classProtected || action.GetCustomAttributes<AuthorizeAttribute>().Any(),
                    $"{controller.Name}.{action.Name} has no explicit authorization.");
            }
        }
    }

    private sealed class FakeUser(Guid? id, bool admin, bool staff) : ICurrentUser
    {
        public Guid? UserId { get; } = id;
        public bool IsAuthenticated => id is not null;
        public bool IsAdmin { get; } = admin;
        public bool IsStaff { get; } = staff;
    }

    private sealed class FakeSessions(Shift? shift) : ISessionRepository
    {
        public Task<Shift?> GetShiftAsync(Guid shiftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(shift);
        public Task<Session?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Session?> GetActiveByDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Device?> GetDeviceForStartAsync(Guid deviceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task AddAsync(Session session, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static Shift OpenShiftFor(Guid staffId)
    {
        var day = new BusinessDay(DateOnly.FromDateTime(DateTime.UtcNow));
        return day.OpenShift(staffId, 100m);
    }

    [Fact]
    public async Task ShiftAccess_StaffOnlyOperatesOnOwnOpenShift()
    {
        var staffId = Guid.NewGuid();
        var own = OpenShiftFor(staffId);
        var others = OpenShiftFor(Guid.NewGuid());

        Assert.True(await new ShiftAccess(new FakeUser(staffId, false, true), new FakeSessions(own))
            .CanOperateOnShiftAsync(own.Id, default));
        Assert.False(await new ShiftAccess(new FakeUser(staffId, false, true), new FakeSessions(others))
            .CanOperateOnShiftAsync(others.Id, default));
        Assert.False(await new ShiftAccess(new FakeUser(staffId, false, true), new FakeSessions(null))
            .CanOperateOnShiftAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task ShiftAccess_StaffCannotOperateOnClosedShiftButCanCloseOwn()
    {
        var staffId = Guid.NewGuid();
        var shift = OpenShiftFor(staffId);
        var access = new ShiftAccess(new FakeUser(staffId, false, true), new FakeSessions(shift));

        Assert.True(await access.CanCloseShiftAsync(shift.Id, default));
        shift.Close(100m, null);
        Assert.False(await access.CanOperateOnShiftAsync(shift.Id, default));
    }

    [Fact]
    public async Task ShiftAccess_AdminAllowedUnauthenticatedAndRolelessDenied()
    {
        var shift = OpenShiftFor(Guid.NewGuid());
        var sessions = new FakeSessions(shift);

        Assert.True(await new ShiftAccess(new FakeUser(Guid.NewGuid(), true, false), sessions)
            .CanOperateOnShiftAsync(shift.Id, default));
        Assert.False(await new ShiftAccess(new FakeUser(null, false, false), sessions)
            .CanOperateOnShiftAsync(shift.Id, default));
        Assert.False(await new ShiftAccess(new FakeUser(Guid.NewGuid(), false, false), sessions)
            .CanOperateOnShiftAsync(shift.Id, default));
    }

    [Fact]
    public void CurrentUser_ReadsIdentityOnlyFromClaims()
    {
        var id = Guid.NewGuid();
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = Principal(id, "Staff") }
        };
        var user = new HttpContextCurrentUser(accessor);

        Assert.True(user.IsAuthenticated);
        Assert.Equal(id, user.UserId);
        Assert.True(user.IsStaff);
        Assert.False(user.IsAdmin);

        var anonymous = new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        Assert.False(anonymous.IsAuthenticated);
        Assert.Null(anonymous.UserId);
    }
}
