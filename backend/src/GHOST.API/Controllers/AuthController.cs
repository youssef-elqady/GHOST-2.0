using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GHOST.API.Authentication;
using GHOST.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly JwtTokenService _tokenService;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        JwtTokenService tokenService)
    {
        _userManager = userManager;
        _tokenService = tokenService;
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
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
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
        if (user is null || !user.IsActive)
            return Unauthorized(new { error = "invalid_credentials" });

        if (!user.LockoutEnabled)
        {
            var lockoutEnabled = await _userManager.SetLockoutEnabledAsync(user, true);
            if (!lockoutEnabled.Succeeded)
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { error = "authentication_unavailable" });
        }

        if (await _userManager.IsLockedOutAsync(user))
            return LockedOut();

        var passwordIsValid = await _userManager.CheckPasswordAsync(
            user,
            request.Password);

        if (!passwordIsValid)
        {
            await _userManager.AccessFailedAsync(user);

            if (await _userManager.IsLockedOutAsync(user))
                return LockedOut();

            return Unauthorized(new { error = "invalid_credentials" });
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        if (user.MustChangePassword)
        {
            var passwordChangeToken = _tokenService.CreatePasswordChangeToken(user);

            return Ok(new PasswordChangeRequiredResponse(
                "password_change_required",
                "Bearer",
                passwordChangeToken.Value,
                passwordChangeToken.ExpiresAtUtc));
        }

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _tokenService.CreateAccessToken(user, roles);

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
        [FromBody] ChangePasswordRequest request)
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
            return Forbid();

        if (await _userManager.IsLockedOutAsync(user))
            return LockedOut();

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
                    return LockedOut();
            }

            return BadRequest(new
            {
                error = "password_change_rejected",
                codes = changeResult.Errors.Select(error => error.Code).ToArray()
            });
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _tokenService.CreateAccessToken(user, roles);

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

    private ObjectResult LockedOut() =>
        StatusCode(StatusCodes.Status423Locked, new { error = "account_locked" });
}
