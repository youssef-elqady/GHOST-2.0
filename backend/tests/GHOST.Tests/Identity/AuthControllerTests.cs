using GHOST.API.Authentication;
using GHOST.API.Controllers;
using GHOST.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace GHOST.Tests.Identity;

public sealed class AuthControllerTests
{
    [Fact]
    public async Task Login_UnknownUser_ReturnsGenericUnauthorizedAndAuditsFailure()
    {
        var userManager = new FakeUserManager(user: null);
        var audit = new FakeAuthenticationAuditWriter();
        var controller = CreateController(userManager, audit);

        var result = await controller.Login(
            new AuthController.LoginRequest("missing", "not-a-real-password"),
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Contains(audit.SystemEvents, item => item.Action == "Authentication.LoginFailed");
        Assert.False(userManager.PasswordCheckCalled);
    }

    [Fact]
    public async Task Login_InactiveUser_IsDeniedBeforePasswordVerification()
    {
        var user = CreateUser(isActive: false);
        var userManager = new FakeUserManager(user);
        var audit = new FakeAuthenticationAuditWriter();
        var controller = CreateController(userManager, audit);

        var result = await controller.Login(
            new AuthController.LoginRequest("staff", "correct-password"),
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.False(userManager.PasswordCheckCalled);
        Assert.Contains(audit.SystemEvents, item =>
            item.Action == "Authentication.LoginDenied.InactiveAccount" &&
            item.TargetUserId == user.Id);
    }

    [Fact]
    public async Task Login_LockedUser_IsDeniedBeforePasswordVerification()
    {
        var user = CreateUser();
        var userManager = new FakeUserManager(user, lockedOut: true);
        var audit = new FakeAuthenticationAuditWriter();
        var controller = CreateController(userManager, audit);

        var result = await controller.Login(
            new AuthController.LoginRequest("staff", "correct-password"),
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.False(userManager.PasswordCheckCalled);
        Assert.Contains(audit.SystemEvents, item =>
            item.Action == "Authentication.LoginDenied.LockedOut");
    }

    [Fact]
    public async Task Login_InvalidPassword_IncrementsFailureCountAndDoesNotIssueToken()
    {
        var user = CreateUser();
        var userManager = new FakeUserManager(user, passwordValid: false);
        var audit = new FakeAuthenticationAuditWriter();
        var issuer = new FakeAccessTokenIssuer();
        var controller = CreateController(userManager, audit, issuer);

        var result = await controller.Login(
            new AuthController.LoginRequest("staff", "incorrect-password"),
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(1, userManager.AccessFailedCount);
        Assert.Null(issuer.LastRoles);
        Assert.Contains(audit.SystemEvents, item =>
            item.Action == "Authentication.LoginFailed" &&
            item.TargetUserId == user.Id);
    }

    [Fact]
    public async Task Login_ValidAdmin_ResetsFailureCountIssuesTokenAndAuditsSuccess()
    {
        var user = CreateUser();
        var userManager = new FakeUserManager(user, passwordValid: true, roles: ["Admin"]);
        var audit = new FakeAuthenticationAuditWriter();
        var issuer = new FakeAccessTokenIssuer();
        var controller = CreateController(userManager, audit, issuer);

        var result = await controller.Login(
            new AuthController.LoginRequest("admin", "correct-password"),
            CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<AuthController.LoginResponse>(response.Value);

        Assert.Equal("test-access-token", payload.AccessToken);
        Assert.Equal("Bearer", payload.TokenType);
        Assert.False(payload.MustChangePassword);
        Assert.Equal(new[] { "Admin" }, payload.Roles);
        Assert.True(userManager.ResetFailureCountCalled);
        Assert.Equal(new[] { "Admin" }, issuer.LastRoles);
        Assert.Contains(audit.UserEvents, item =>
            item.UserId == user.Id &&
            item.Action == "Authentication.LoginSucceeded");
    }

    [Fact]
    public async Task Login_MustChangePassword_IssuesRestrictedTokenEvenBeforeRoleAssignment()
    {
        var user = CreateUser(mustChangePassword: true);
        var userManager = new FakeUserManager(user, passwordValid: true, roles: []);
        var audit = new FakeAuthenticationAuditWriter();
        var issuer = new FakeAccessTokenIssuer();
        var controller = CreateController(userManager, audit, issuer);

        var result = await controller.Login(
            new AuthController.LoginRequest("new-staff", "temporary-password"),
            CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<AuthController.LoginResponse>(response.Value);

        Assert.True(payload.MustChangePassword);
        Assert.Empty(payload.Roles);
        Assert.Empty(issuer.LastRoles!);
        Assert.Contains(audit.UserEvents, item => item.Action == "Authentication.LoginSucceeded");
    }

    [Fact]
    public async Task Login_FifthFailedAttempt_RecordsLockoutEvent()
    {
        var user = CreateUser();
        var userManager = new FakeUserManager(user, passwordValid: false, initialFailures: 4);
        var audit = new FakeAuthenticationAuditWriter();
        var controller = CreateController(userManager, audit);

        var result = await controller.Login(
            new AuthController.LoginRequest("staff", "incorrect-password"),
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(5, userManager.AccessFailedCount);
        Assert.Contains(audit.SystemEvents, item =>
            item.Action == "Authentication.AccountLocked");
    }

    private static AuthController CreateController(
        FakeUserManager userManager,
        FakeAuthenticationAuditWriter audit,
        FakeAccessTokenIssuer? issuer = null) =>
        new(userManager, audit, issuer ?? new FakeAccessTokenIssuer());

    private static ApplicationUser CreateUser(bool isActive = true, bool mustChangePassword = false) =>
        new()
        {
            UserName = "staff",
            FullName = "GHOST Staff",
            IsActive = isActive,
            MustChangePassword = mustChangePassword
        };

    private sealed class FakeAuthenticationAuditWriter : IAuthenticationAuditWriter
    {
        public List<(string Action, Guid? TargetUserId)> SystemEvents { get; } = [];
        public List<(Guid UserId, string Action)> UserEvents { get; } = [];

        public Task WriteSystemAsync(string action, Guid? targetUserId, CancellationToken cancellationToken)
        {
            SystemEvents.Add((action, targetUserId));
            return Task.CompletedTask;
        }

        public Task WriteUserAsync(Guid userId, string action, CancellationToken cancellationToken)
        {
            UserEvents.Add((userId, action));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccessTokenIssuer : IAccessTokenIssuer
    {
        public IReadOnlyCollection<string>? LastRoles { get; private set; }

        public IssuedAccessToken Issue(ApplicationUser user, IReadOnlyCollection<string> roles)
        {
            LastRoles = roles.ToArray();
            return new IssuedAccessToken("test-access-token", DateTimeOffset.UtcNow.AddMinutes(10));
        }
    }

    private sealed class FakeUserManager : UserManager<ApplicationUser>
    {
        private readonly ApplicationUser? _user;
        private readonly bool _passwordValid;
        private readonly IList<string> _roles;

        public FakeUserManager(
            ApplicationUser? user,
            bool passwordValid = true,
            bool lockedOut = false,
            IList<string>? roles = null,
            int initialFailures = 0)
            : base(
                new FakeUserStore(),
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                [],
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                new ServiceCollection().BuildServiceProvider(),
                NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            _user = user;
            _passwordValid = passwordValid;
            LockedOut = lockedOut;
            _roles = roles ?? [];
            AccessFailedCount = initialFailures;
        }

        public bool PasswordCheckCalled { get; private set; }
        public bool ResetFailureCountCalled { get; private set; }
        public bool LockedOut { get; private set; }
        public int AccessFailedCount { get; private set; }

        public override Task<ApplicationUser?> FindByNameAsync(string userName) =>
            Task.FromResult(_user);

        public override Task<bool> CheckPasswordAsync(ApplicationUser user, string password)
        {
            PasswordCheckCalled = true;
            return Task.FromResult(_passwordValid);
        }

        public override Task<bool> IsLockedOutAsync(ApplicationUser user) =>
            Task.FromResult(LockedOut);

        public override Task<IdentityResult> AccessFailedAsync(ApplicationUser user)
        {
            AccessFailedCount++;
            if (AccessFailedCount >= 5)
                LockedOut = true;

            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<IdentityResult> ResetAccessFailedCountAsync(ApplicationUser user)
        {
            AccessFailedCount = 0;
            ResetFailureCountCalled = true;
            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<IList<string>> GetRolesAsync(ApplicationUser user) =>
            Task.FromResult(_roles);
    }

    private sealed class FakeUserStore : IUserStore<ApplicationUser>
    {
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<ApplicationUser?>(null);

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult<ApplicationUser?>(null);

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id.ToString());

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
