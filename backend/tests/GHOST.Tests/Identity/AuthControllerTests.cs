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
            new AuthController.LoginRequest(user.UserName, InitialPassword));

        var response = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<AuthController.AuthenticatedResponse>(response.Value);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(payload.AccessToken);

        Assert.Equal("authenticated", payload.Status);
        Assert.Equal(user.Id, payload.User.Id);
        Assert.Contains("Admin", payload.User.Roles);
        Assert.Contains(jwt.Claims, claim => claim.Type == "token_use" && claim.Value == "access");
        Assert.Contains(jwt.Claims, claim => claim.Type == ClaimTypes.Role && claim.Value == "Admin");
        Assert.True(payload.ExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_ReturnsRestrictedPasswordChangeTokenWhenRequired()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync(mustChangePassword: true);

        var result = await fixture.Controller.Login(
            new AuthController.LoginRequest(user.UserName, InitialPassword));

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
    }

    [Fact]
    public async Task ChangePassword_ConsumesRestrictedFlowAndReturnsNormalAccessToken()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync(mustChangePassword: true);
        var controller = fixture.CreatePasswordChangeController(user.Id);

        var result = await controller.ChangePassword(
            new AuthController.ChangePasswordRequest(InitialPassword, NewPassword));

        var response = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<AuthController.AuthenticatedResponse>(response.Value);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(payload.AccessToken);
        var refreshedUser = await fixture.UserManager.FindByIdAsync(user.Id.ToString());

        Assert.Equal("authenticated", payload.Status);
        Assert.Contains(jwt.Claims, claim => claim.Type == "token_use" && claim.Value == "access");
        Assert.False(refreshedUser!.MustChangePassword);
        Assert.True(await fixture.UserManager.CheckPasswordAsync(refreshedUser, NewPassword));
        Assert.False(await fixture.UserManager.CheckPasswordAsync(refreshedUser, InitialPassword));
    }

    [Fact]
    public async Task Login_RejectsInactiveAccountWithoutIssuingToken()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var user = await fixture.CreateUserAsync(isActive: false);

        var result = await fixture.Controller.Login(
            new AuthController.LoginRequest(user.UserName, InitialPassword));

        Assert.IsType<UnauthorizedObjectResult>(result);
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
                new AuthController.LoginRequest(user.UserName, "Wrong!Password123"));
        }

        var locked = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status423Locked, locked.StatusCode);
        Assert.True(await fixture.UserManager.IsLockedOutAsync(user));
    }

    private sealed class IdentityFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        public UserManager<ApplicationUser> UserManager { get; }
        public AuthController Controller { get; }

        private IdentityFixture(
            ServiceProvider provider,
            AsyncServiceScope scope,
            UserManager<ApplicationUser> userManager)
        {
            _provider = provider;
            _scope = scope;
            UserManager = userManager;

            var tokenService = new JwtTokenService(Options.Create(new JwtOptions
            {
                Issuer = "GHOST.Tests",
                Audience = "GHOST.Tests.Client",
                SigningKey = Convert.ToBase64String(new byte[32]),
                AccessTokenLifetimeMinutes = 10
            }));

            Controller = new AuthController(UserManager, tokenService);
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

            var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>("Admin"));
            Assert.True(roleResult.Succeeded);

            return new IdentityFixture(provider, scope, userManager);
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

            return new AuthController(
                UserManager,
                new JwtTokenService(Options.Create(new JwtOptions
                {
                    Issuer = "GHOST.Tests",
                    Audience = "GHOST.Tests.Client",
                    SigningKey = Convert.ToBase64String(new byte[32]),
                    AccessTokenLifetimeMinutes = 10
                })))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = principal }
                }
            };
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }
}
