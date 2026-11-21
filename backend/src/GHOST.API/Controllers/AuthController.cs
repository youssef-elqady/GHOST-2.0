using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GHOST.API.Authentication;
using GHOST.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GHOST.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly JwtTokenService _tokenService;
    private readonly IAuthenticationAuditWriter _auditWriter;
    private readonly RefreshTokenService _refreshTokens;
    private readonly RefreshTokenOptions _refreshOptions;
    private readonly IHostEnvironment _environment;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        JwtTokenService tokenService,
        IAuthenticationAuditWriter auditWriter,
        RefreshTokenService refreshTokens,
        IOptions<RefreshTokenOptions> refreshOptions,
        IHostEnvironment environment)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _auditWriter = auditWriter;
        _refreshTokens = refreshTokens;
        _refreshOptions = refreshOptions.Value;
        _environment = environment;
    }

    public sealed record LoginRequest(string? Username, string? Password);

    public sealed record ChangePasswordRequest(
        string? CurrentPassword,
        string? NewPassword);

    public sealed record AuthenticatedUserResponse(
        Guid Id,
        string Username,
        string FullName,
        IReadOnlyCollection<string> Roles);

    public sealed record AuthenticatedResponse(
        string Status,
        string TokenType,
        string AccessToken,
        DateTimeOffset ExpiresAtUtc,
        AuthenticatedUserResponse User);

    public sealed record PasswordChangeRequiredResponse(
        string Status,
        string TokenType,
        string PasswordChangeToken,
        DateTimeOffset ExpiresAtUtc);

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            request.Username.Length > 256 ||
            request.Password.Length > 128)
        {
            return BadRequest(new { error = "invalid_request" });
        }

        var user = await _userManager.FindByNameAsync(request.Username.Trim());

        // Do not disclose whether an account exists or is disabled.
        if (user is null)
        {
            await _auditWriter.WriteSystemAsync(
                "Authentication.LoginFailed",
                null,
                cancellationToken);
            return Unauthorized(new { error = "invalid_credentials" });
        }

        if (!user.IsActive)
        {
            await _auditWriter.WriteSystemAsync(
                "Authentication.LoginDenied.InactiveAccount",
                user.Id,
                cancellationToken);
            return Unauthorized(new { error = "invalid_credentials" });
        }

        if (!user.LockoutEnabled)
        {
            var lockoutEnabled = await _userManager.SetLockoutEnabledAsync(user, true);
            if (!lockoutEnabled.Succeeded)
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { error = "authentication_unavailable" });
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            await _auditWriter.WriteSystemAsync(
                "Authentication.LoginDenied.LockedOut",
                user.Id,
                cancellationToken);
            return LockedOut();
        }

        var passwordIsValid = await _userManager.CheckPasswordAsync(
            user,
            request.Password);

        if (!passwordIsValid)
        {
            await _userManager.AccessFailedAsync(user);

            var lockedOut = await _userManager.IsLockedOutAsync(user);
            await _auditWriter.WriteSystemAsync(
                lockedOut
                    ? "Authentication.AccountLocked"
                    : "Authentication.LoginFailed",
                user.Id,
                cancellationToken);

            if (lockedOut)
                return LockedOut();

            return Unauthorized(new { error = "invalid_credentials" });
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        if (user.MustChangePassword)
        {
            var passwordChangeToken = _tokenService.CreatePasswordChangeToken(user);
            await _auditWriter.WriteUserAsync(
                user.Id,
                "Authentication.PasswordChangeTokenIssued",
                cancellationToken);

            return Ok(new PasswordChangeRequiredResponse(
                "password_change_required",
                "Bearer",
                passwordChangeToken.Value,
                passwordChangeToken.ExpiresAtUtc));
        }

        var roles = await _userManager.GetRolesAsync(user);
        var refreshToken = await _refreshTokens.StartFamilyAsync(user.Id, cancellationToken);
        SetRefreshCookie(refreshToken);
        var accessToken = _tokenService.CreateAccessToken(user, roles, refreshToken.FamilyId);
        await _auditWriter.WriteUserAsync(
            user.Id,
            "Authentication.LoginSucceeded",
            cancellationToken);

        return Ok(new AuthenticatedResponse(
            "authenticated",
            "Bearer",
            accessToken.Value,
            accessToken.ExpiresAtUtc,
            new AuthenticatedUserResponse(
                user.Id,
                user.UserName ?? string.Empty,
                user.FullName,
                roles.ToArray())));
    }

    [Authorize(Policy = "PasswordChangeOnly")]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.CurrentPassword) ||
            string.IsNullOrWhiteSpace(request.NewPassword) ||
            request.CurrentPassword.Length > 128 ||
            request.NewPassword.Length > 128)
        {
            return BadRequest(new { error = "invalid_request" });
        }

        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId.ToString("D"));
        if (user is null || !user.IsActive || !user.MustChangePassword)
        {
            await _auditWriter.WriteSystemAsync(
                "Authentication.PasswordChangeDenied",
                userId,
                cancellationToken);
            return Forbid();
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            await _auditWriter.WriteSystemAsync(
                "Authentication.PasswordChangeDenied.LockedOut",
                user.Id,
                cancellationToken);
            return LockedOut();
        }

        // Persist the flag change together with the password hash update performed by Identity.
        user.MustChangePassword = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var changeResult = await _userManager.ChangePasswordAsync(
            user,
            request.CurrentPassword,
            request.NewPassword);

        if (!changeResult.Succeeded)
        {
            user.MustChangePassword = true;

            if (changeResult.Errors.Any(error => error.Code == "PasswordMismatch"))
            {
                await _userManager.AccessFailedAsync(user);
                if (await _userManager.IsLockedOutAsync(user))
                {
                    await _auditWriter.WriteSystemAsync(
                        "Authentication.AccountLocked",
                        user.Id,
                        cancellationToken);
                    return LockedOut();
                }
            }

            await _auditWriter.WriteUserAsync(
                user.Id,
                "Authentication.PasswordChangeFailed",
                cancellationToken);

            return BadRequest(new
            {
                error = "password_change_rejected",
                codes = changeResult.Errors.Select(error => error.Code).ToArray()
            });
        }

        await _userManager.ResetAccessFailedCountAsync(user);
        await _auditWriter.WriteUserAsync(
            user.Id,
            "Authentication.PasswordChanged",
            cancellationToken);

        // A password change ends every older session and starts a fresh family.
        await _refreshTokens.RevokeAllForUserAsync(
            user.Id,
            RefreshTokenService.SessionsRevokedReason,
            cancellationToken);
        await _auditWriter.WriteUserAsync(
            user.Id,
            "Authentication.SessionsRevoked",
            cancellationToken);

        var roles = await _userManager.GetRolesAsync(user);
        var refreshToken = await _refreshTokens.StartFamilyAsync(user.Id, cancellationToken);
        SetRefreshCookie(refreshToken);
        var accessToken = _tokenService.CreateAccessToken(user, roles, refreshToken.FamilyId);

        return Ok(new AuthenticatedResponse(
            "authenticated",
            "Bearer",
            accessToken.Value,
            accessToken.ExpiresAtUtc,
            new AuthenticatedUserResponse(
                user.Id,
                user.UserName ?? string.Empty,
                user.FullName,
                roles.ToArray())));
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!RefreshRequestGuard.IsTrusted(Request, _refreshOptions))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "untrusted_request" });

        Request.Cookies.TryGetValue(RefreshTokenOptions.CookieName, out var rawToken);
        var result = await _refreshTokens.RotateAsync(rawToken, cancellationToken);

        switch (result.Outcome)
        {
            case RefreshOutcome.ReplayDetected:
                await _auditWriter.WriteSystemAsync(
                    "Authentication.RefreshReplayDetected",
                    result.UserId,
                    cancellationToken);
                await _auditWriter.WriteSystemAsync(
                    "Authentication.RefreshFamilyRevoked",
                    result.UserId,
                    cancellationToken);
                return RefreshRejected();

            case RefreshOutcome.Rejected:
                await _auditWriter.WriteSystemAsync(
                    "Authentication.RefreshRejected",
                    result.UserId,
                    cancellationToken);
                return RefreshRejected();
        }

        var user = await _userManager.FindByIdAsync(result.UserId!.Value.ToString("D"));
        if (user is null ||
            !user.IsActive ||
            user.MustChangePassword ||
            await _userManager.IsLockedOutAsync(user))
        {
            await _refreshTokens.RevokeAllForUserAsync(
                result.UserId.Value,
                RefreshTokenService.SessionsRevokedReason,
                cancellationToken);
            await _auditWriter.WriteSystemAsync(
                "Authentication.RefreshRejected",
                result.UserId,
                cancellationToken);
            return RefreshRejected();
        }

        SetRefreshCookie(result.Replacement!);

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _tokenService.CreateAccessToken(user, roles, result.FamilyId!.Value);
        await _auditWriter.WriteUserAsync(
            user.Id,
            "Authentication.RefreshSucceeded",
            cancellationToken);

        return Ok(new AuthenticatedResponse(
            "authenticated",
            "Bearer",
            accessToken.Value,
            accessToken.ExpiresAtUtc,
            new AuthenticatedUserResponse(
                user.Id,
                user.UserName ?? string.Empty,
                user.FullName,
                roles.ToArray())));
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (!RefreshRequestGuard.IsTrusted(Request, _refreshOptions))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "untrusted_request" });

        Request.Cookies.TryGetValue(RefreshTokenOptions.CookieName, out var rawToken);
        var revoked = await _refreshTokens.RevokeFamilyByTokenAsync(
            rawToken,
            RefreshTokenService.LogoutReason,
            cancellationToken);

        if (revoked is not null)
        {
            await _auditWriter.WriteUserAsync(
                revoked.UserId,
                "Authentication.Logout",
                cancellationToken);
        }

        ClearRefreshCookie();
        return NoContent();
    }

    private void SetRefreshCookie(IssuedRefreshToken token) =>
        Response.Cookies.Append(
            RefreshTokenOptions.CookieName,
            token.RawValue,
            RefreshRequestGuard.CreateCookieOptions(
                _environment.IsDevelopment(),
                token.ExpiresAtUtc));

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(
            RefreshTokenOptions.CookieName,
            RefreshRequestGuard.CreateCookieOptions(_environment.IsDevelopment(), null));

    private IActionResult RefreshRejected()
    {
        ClearRefreshCookie();
        return Unauthorized(new { error = "invalid_session" });
    }

    private ObjectResult LockedOut() =>
        StatusCode(StatusCodes.Status423Locked, new { error = "account_locked" });
}
