using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GHOST.API.Authentication;
using GHOST.API.Controllers;
using GHOST.Infrastructure.Identity;
using GHOST.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GHOST.Tests.Identity;

public sealed class AuthControllerTests
{
    private const string InitialPassword = "Strong!Pass123";
    private const string NewPassword = "NewStrong!Pass456";

    [Fact]
    public async Task Login_IssuesAccessTokenWithRolesForActiveUser()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync();

        var result = await fixture.Controller.Login(
            new AuthController.LoginRequest(user.UserName, InitialPassword), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<AuthController.AuthenticatedResponse>(response.Value);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(payload.AccessToken);

        Assert.Equal("authenticated", payload.Status);
        Assert.Equal(user.Id, payload.User.Id);
        Assert.Contains("Admin", payload.User.Roles);
        Assert.Contains(jwt.Claims, claim => claim.Type == "token_use" && claim.Value == "access");
        Assert.Contains(jwt.Claims, claim => claim.Type == "role" && claim.Value == "Admin");
        Assert.True(payload.ExpiresAtUtc > DateTimeOffset.UtcNow);

        var audit = await fixture.DbContext.AuditLogs.SingleAsync(
            item => item.Action == "Authentication.LoginSucceeded");
        Assert.Equal("User", audit.ActorType);
        Assert.Equal(user.Id, audit.UserId);
        Assert.Null(audit.OldValues);
        Assert.Null(audit.NewValues);
    }

    [Fact]
    public async Task Login_ReturnsRestrictedPasswordChangeTokenWhenRequired()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync(mustChangePassword: true);

        var result = await fixture.Controller.Login(
            new AuthController.LoginRequest(user.UserName, InitialPassword), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<AuthController.PasswordChangeRequiredResponse>(response.Value);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(payload.PasswordChangeToken);

        Assert.Equal("password_change_required", payload.Status);
        Assert.Contains(jwt.Claims, claim =>
            claim.Type == "token_use" && claim.Value == "password_change");
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type == ClaimTypes.Role);
        Assert.InRange(
            payload.ExpiresAtUtc - DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(5));
        Assert.Contains(await fixture.DbContext.AuditLogs.ToListAsync(), item =>
            item.Action == "Authentication.PasswordChangeTokenIssued" && item.UserId == user.Id);
    }

    [Fact]
    public async Task ChangePassword_ConsumesRestrictedFlowAndReturnsNormalAccessToken()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync(mustChangePassword: true);
        var controller = fixture.CreatePasswordChangeController(user.Id);

        var result = await controller.ChangePassword(
            new AuthController.ChangePasswordRequest(InitialPassword, NewPassword), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<AuthController.AuthenticatedResponse>(response.Value);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(payload.AccessToken);
        var refreshedUser = await fixture.UserManager.FindByIdAsync(user.Id.ToString());

        Assert.Equal("authenticated", payload.Status);
        Assert.Contains(jwt.Claims, claim => claim.Type == "token_use" && claim.Value == "access");
        Assert.False(refreshedUser!.MustChangePassword);
        Assert.True(await fixture.UserManager.CheckPasswordAsync(refreshedUser, NewPassword));
        Assert.False(await fixture.UserManager.CheckPasswordAsync(refreshedUser, InitialPassword));
        Assert.Contains(await fixture.DbContext.AuditLogs.ToListAsync(), item =>
            item.Action == "Authentication.PasswordChanged" && item.UserId == user.Id);
    }

    [Fact]
    public async Task Login_RejectsInactiveAccountWithoutIssuingToken()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync(isActive: false);

        var result = await fixture.Controller.Login(
            new AuthController.LoginRequest(user.UserName, InitialPassword), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Contains(await fixture.DbContext.AuditLogs.ToListAsync(), item =>
            item.Action == "Authentication.LoginDenied.InactiveAccount" &&
            item.ActorType == "System" && item.UserId is null && item.EntityId == user.Id.ToString());
    }

    [Fact]
    public async Task Login_UnknownUser_IsAuditedWithoutPersistingUsernameOrCredentials()
    {
        await using var fixture = await IdentityFixture.CreateAsync();

        var result = await fixture.Controller.Login(
            new AuthController.LoginRequest("missing-user", "Sensitive!Password123"),
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        var audit = await fixture.DbContext.AuditLogs.SingleAsync(
            item => item.Action == "Authentication.LoginFailed");
        Assert.Equal("System", audit.ActorType);
        Assert.Null(audit.UserId);
        Assert.Null(audit.EntityId);
        Assert.Null(audit.OldValues);
        Assert.Null(audit.NewValues);
    }

    [Fact]
    public async Task Login_LocksAccountAfterFiveFailedPasswordAttempts()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync();

        IActionResult? result = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            result = await fixture.Controller.Login(
                new AuthController.LoginRequest(user.UserName, "Wrong!Password123"), CancellationToken.None);
        }

        var locked = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status423Locked, locked.StatusCode);
        Assert.True(await fixture.UserManager.IsLockedOutAsync(user));
        Assert.Equal(4, await fixture.DbContext.AuditLogs.CountAsync(
            item => item.Action == "Authentication.LoginFailed"));
        Assert.Single(await fixture.DbContext.AuditLogs.Where(
            item => item.Action == "Authentication.AccountLocked").ToListAsync());
    }

    private static DefaultHttpContext TrustedContext(string? rawRefreshToken)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Origin = "http://localhost:3000";
        context.Request.Headers[RefreshTokenOptions.RequestHeaderName] =
            RefreshTokenOptions.RequestHeaderValue;
        if (rawRefreshToken is not null)
            context.Request.Headers.Cookie = $"{RefreshTokenOptions.CookieName}={rawRefreshToken}";
        return context;
    }

    private static string? ExtractRefreshCookie(HttpContext context)
    {
        var header = context.Response.Headers.SetCookie.ToString();
        var prefix = RefreshTokenOptions.CookieName + "=";
        var start = header.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return null;
        var value = header[(start + prefix.Length)..].Split(';')[0];
        return value.Length == 0 ? null : value;
    }

    [Fact]
    public async Task Login_SetsHttpOnlyStrictRefreshCookieAndSessionClaim()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync();
        var http = new DefaultHttpContext();
        var controller = fixture.CreateController(fixture.CreateTokenService(), http);

        var result = await controller.Login(
            new AuthController.LoginRequest(user.UserName, InitialPassword), CancellationToken.None);

        var payload = Assert.IsType<AuthController.AuthenticatedResponse>(
            Assert.IsType<OkObjectResult>(result).Value);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(payload.AccessToken);
        var cookie = http.Response.Headers.SetCookie.ToString();

        Assert.Contains(jwt.Claims, claim => claim.Type == "sid");
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(payload.AccessToken, cookie);

        var raw = ExtractRefreshCookie(http);
        Assert.NotNull(raw);
        var stored = await fixture.DbContext.RefreshTokens.SingleAsync();
        Assert.Equal(RefreshTokenService.Hash(raw!), stored.TokenHash);
        Assert.NotEqual(raw, stored.TokenHash);
    }

    [Fact]
    public async Task Refresh_RotatesTokenAndReturnsNewAccessToken()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync();
        var first = await fixture.RefreshTokens.StartFamilyAsync(user.Id, CancellationToken.None);
        var http = TrustedContext(first.RawValue);
        var controller = fixture.CreateController(fixture.CreateTokenService(), http);

        var result = await controller.Refresh(CancellationToken.None);

        var payload = Assert.IsType<AuthController.AuthenticatedResponse>(
            Assert.IsType<OkObjectResult>(result).Value);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(payload.AccessToken);
        var rotated = ExtractRefreshCookie(http);

        Assert.NotNull(rotated);
        Assert.NotEqual(first.RawValue, rotated);
        Assert.Contains(jwt.Claims, claim =>
            claim.Type == "sid" && claim.Value == first.FamilyId.ToString("D"));
        Assert.Equal(2, await fixture.DbContext.RefreshTokens.CountAsync());
        Assert.Contains(await fixture.DbContext.AuditLogs.ToListAsync(), item =>
            item.Action == "Authentication.RefreshSucceeded");
    }

    [Fact]
    public async Task Refresh_RejectsUntrustedOriginOrMissingHeader()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync();
        var first = await fixture.RefreshTokens.StartFamilyAsync(user.Id, CancellationToken.None);

        var wrongOrigin = TrustedContext(first.RawValue);
        wrongOrigin.Request.Headers.Origin = "https://evil.example";
        var missingHeader = TrustedContext(first.RawValue);
        missingHeader.Request.Headers.Remove(RefreshTokenOptions.RequestHeaderName);

        foreach (var http in new[] { wrongOrigin, missingHeader })
        {
            var controller = fixture.CreateController(fixture.CreateTokenService(), http);
            var result = await controller.Refresh(CancellationToken.None);
            Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        Assert.Equal(1, await fixture.DbContext.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task Refresh_ReplayOfConsumedTokenRevokesWholeFamily()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync();
        var first = await fixture.RefreshTokens.StartFamilyAsync(user.Id, CancellationToken.None);

        var second = await fixture.RefreshTokens.RotateAsync(first.RawValue, CancellationToken.None);
        Assert.Equal(RefreshOutcome.Rotated, second.Outcome);

        var replay = await fixture.RefreshTokens.RotateAsync(first.RawValue, CancellationToken.None);
        Assert.Equal(RefreshOutcome.ReplayDetected, replay.Outcome);

        // The legitimate descendant is also dead after replay detection.
        var descendant = await fixture.RefreshTokens.RotateAsync(
            second.Replacement!.RawValue, CancellationToken.None);
        Assert.Equal(RefreshOutcome.ReplayDetected, descendant.Outcome);
        Assert.All(await fixture.DbContext.RefreshTokens.ToListAsync(), token => Assert.True(token.IsRevoked));
        Assert.False(await fixture.RefreshTokens.IsFamilyActiveAsync(
            user.Id, first.FamilyId, CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_ReplayEndpointReturns401ClearsCookieAndAudits()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync();
        var first = await fixture.RefreshTokens.StartFamilyAsync(user.Id, CancellationToken.None);
        await fixture.RefreshTokens.RotateAsync(first.RawValue, CancellationToken.None);

        var http = TrustedContext(first.RawValue);
        var controller = fixture.CreateController(fixture.CreateTokenService(), http);
        var result = await controller.Refresh(CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Contains(await fixture.DbContext.AuditLogs.ToListAsync(), item =>
            item.Action == "Authentication.RefreshReplayDetected");
        Assert.Contains("expires=", http.Response.Headers.SetCookie.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_UnknownOrMissingTokenIsRejected()
    {
        await using var fixture = await IdentityFixture.CreateAsync();

        Assert.Equal(RefreshOutcome.Rejected,
            (await fixture.RefreshTokens.RotateAsync(null, CancellationToken.None)).Outcome);
        Assert.Equal(RefreshOutcome.Rejected,
            (await fixture.RefreshTokens.RotateAsync("nope", CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Refresh_RejectsAndRevokesWhenUserIsDisabled()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync();
        var first = await fixture.RefreshTokens.StartFamilyAsync(user.Id, CancellationToken.None);
        user.IsActive = false;
        await fixture.UserManager.UpdateAsync(user);

        var controller = fixture.CreateController(
            fixture.CreateTokenService(), TrustedContext(first.RawValue));
        var result = await controller.Refresh(CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.False(await fixture.RefreshTokens.IsFamilyActiveAsync(
            user.Id, first.FamilyId, CancellationToken.None));
    }

    [Fact]
    public async Task Logout_RevokesFamilyAndIsIdempotent()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync();
        var first = await fixture.RefreshTokens.StartFamilyAsync(user.Id, CancellationToken.None);
        Assert.True(await fixture.RefreshTokens.IsFamilyActiveAsync(
            user.Id, first.FamilyId, CancellationToken.None));

        var controller = fixture.CreateController(
            fixture.CreateTokenService(), TrustedContext(first.RawValue));
        Assert.IsType<NoContentResult>(await controller.Logout(CancellationToken.None));
        Assert.False(await fixture.RefreshTokens.IsFamilyActiveAsync(
            user.Id, first.FamilyId, CancellationToken.None));

        var again = fixture.CreateController(
            fixture.CreateTokenService(), TrustedContext(first.RawValue));
        Assert.IsType<NoContentResult>(await again.Logout(CancellationToken.None));

        var noCookie = fixture.CreateController(
            fixture.CreateTokenService(), TrustedContext(null));
        Assert.IsType<NoContentResult>(await noCookie.Logout(CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredTokenAndFamilyAreRejected()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new RefreshTokenService(
            fixture.DbContext, Options.Create(new RefreshTokenOptions()), clock);

        var first = await service.StartFamilyAsync(user.Id, CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(8));

        var result = await service.RotateAsync(first.RawValue, CancellationToken.None);
        Assert.Equal(RefreshOutcome.Rejected, result.Outcome);
        Assert.False(await service.IsFamilyActiveAsync(user.Id, first.FamilyId, CancellationToken.None));
    }

    [Fact]
    public async Task Rotation_NeverExtendsPastAbsoluteFamilyLifetime()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new RefreshTokenService(
            fixture.DbContext, Options.Create(new RefreshTokenOptions()), clock);

        var current = await service.StartFamilyAsync(user.Id, CancellationToken.None);
        var familyEnd = clock.GetUtcNow().AddDays(30);

        for (var day = 0; day < 4; day++)
        {
            clock.Advance(TimeSpan.FromDays(6));
            var rotated = await service.RotateAsync(current.RawValue, CancellationToken.None);
            Assert.Equal(RefreshOutcome.Rotated, rotated.Outcome);
            Assert.True(rotated.Replacement!.ExpiresAtUtc <= familyEnd);
            current = rotated.Replacement;
        }

        clock.Advance(TimeSpan.FromDays(7));
        Assert.Equal(RefreshOutcome.Rejected,
            (await service.RotateAsync(current.RawValue, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task ChangePassword_RevokesOlderSessionsAndStartsNewFamily()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync(mustChangePassword: true);
        var old = await fixture.RefreshTokens.StartFamilyAsync(user.Id, CancellationToken.None);

        var result = await fixture.CreatePasswordChangeController(user.Id).ChangePassword(
            new AuthController.ChangePasswordRequest(InitialPassword, NewPassword), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.False(await fixture.RefreshTokens.IsFamilyActiveAsync(
            user.Id, old.FamilyId, CancellationToken.None));
        Assert.Contains(await fixture.DbContext.RefreshTokens.ToListAsync(), token =>
            token.FamilyId != old.FamilyId && !token.IsRevoked);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class IdentityFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        public UserManager<ApplicationUser> UserManager { get; }
        public GHOSTDbContext DbContext { get; }
        public AuthenticationAuditWriter AuditWriter { get; }
        public AuthController Controller { get; }
        public RefreshTokenService RefreshTokens { get; }

        public AuthController CreateController(JwtTokenService tokenService, HttpContext? httpContext) =>
            new(
                UserManager,
                tokenService,
                AuditWriter,
                RefreshTokens,
                Options.Create(new RefreshTokenOptions()),
                new TestHostEnvironment())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = httpContext ?? new DefaultHttpContext()
                }
            };

        public JwtTokenService CreateTokenService() =>
            new(Options.Create(new JwtOptions
            {
                Issuer = "GHOST.Tests",
                Audience = "GHOST.Tests.Client",
                SigningKey = Convert.ToBase64String(new byte[32]),
                AccessTokenLifetimeMinutes = 10
            }));

        private IdentityFixture(
            ServiceProvider provider,
            AsyncServiceScope scope,
            UserManager<ApplicationUser> userManager,
            GHOSTDbContext dbContext)
        {
            _provider = provider;
            _scope = scope;
            UserManager = userManager;
            DbContext = dbContext;
            AuditWriter = new AuthenticationAuditWriter(dbContext);

            var tokenService = new JwtTokenService(Options.Create(new JwtOptions
            {
                Issuer = "GHOST.Tests",
                Audience = "GHOST.Tests.Client",
                SigningKey = Convert.ToBase64String(new byte[32]),
                AccessTokenLifetimeMinutes = 10
            }));

            RefreshTokens = new RefreshTokenService(
                dbContext, Options.Create(new RefreshTokenOptions()), TimeProvider.System);
            Controller = CreateController(tokenService, null);
        }

        public static async Task<IdentityFixture> CreateAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<GHOSTDbContext>(options =>
                options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
            services.AddGhostIdentity();

            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var dbContext = scope.ServiceProvider.GetRequiredService<GHOSTDbContext>();

            var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>("Admin"));
            Assert.True(roleResult.Succeeded);

            return new IdentityFixture(provider, scope, userManager, dbContext);
        }

        public async Task<ApplicationUser> CreateUserAsync(
            bool isActive = true,
            bool mustChangePassword = false)
        {
            var user = new ApplicationUser
            {
                UserName = $"owner-{Guid.NewGuid():N}",
                FullName = "GHOST Test Owner",
                IsActive = isActive,
                MustChangePassword = mustChangePassword
            };

            var result = await UserManager.CreateAsync(user, InitialPassword);
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));

            var roleResult = await UserManager.AddToRoleAsync(user, "Admin");
            Assert.True(roleResult.Succeeded);

            return user;
        }

        public AuthController CreatePasswordChangeController(Guid userId)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
                new Claim("token_use", "password_change")
            ], "Bearer"));

            return CreateController(
                CreateTokenService(),
                new DefaultHttpContext { User = principal });
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }

    internal sealed class TestHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "GHOST.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
