using System.ComponentModel.DataAnnotations;
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
    private const string InvalidCredentialsMessage = "اسم المستخدم أو كلمة المرور غير صحيحة.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthenticationAuditWriter _auditWriter;
    private readonly IAccessTokenIssuer _tokenIssuer;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        IAuthenticationAuditWriter auditWriter,
        IAccessTokenIssuer tokenIssuer)
    {
        _userManager = userManager;
        _auditWriter = auditWriter;
        _tokenIssuer = tokenIssuer;
    }

    public sealed record LoginRequest(
        [property: Required, StringLength(256, MinimumLength = 1)] string Username,
        [property: Required, StringLength(256, MinimumLength = 1)] string Password);

    public sealed record LoginResponse(
        string AccessToken,
        string TokenType,
        DateTimeOffset ExpiresAt,
        bool MustChangePassword,
        string FullName,
        IReadOnlyCollection<string> Roles);

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = request.Username.Trim();
        var user = await _userManager.FindByNameAsync(normalizedUsername);

        if (user is null)
        {
            await _auditWriter.WriteSystemAsync("Authentication.LoginFailed", null, cancellationToken);
            return Unauthorized(new { error = InvalidCredentialsMessage });
        }

        if (!user.IsActive)
        {
            await WriteSystemAuditAsync(
                "Authentication.LoginDenied.InactiveAccount",
                user.Id,
                cancellationToken);
            return Unauthorized(new { error = InvalidCredentialsMessage });
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            await WriteSystemAuditAsync(
                "Authentication.LoginDenied.LockedOut",
                user.Id,
                cancellationToken);
            return Unauthorized(new { error = InvalidCredentialsMessage });
        }

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            var failedAttempt = await _userManager.AccessFailedAsync(user);
            if (!failedAttempt.Succeeded)
                return Problem(statusCode: StatusCodes.Status500InternalServerError);

            var lockedOut = await _userManager.IsLockedOutAsync(user);

            await WriteSystemAuditAsync(
                lockedOut
                    ? "Authentication.AccountLocked"
                    : "Authentication.LoginFailed",
                user.Id,
                cancellationToken);

            return Unauthorized(new { error = InvalidCredentialsMessage });
        }

        var resetResult = await _userManager.ResetAccessFailedCountAsync(user);
        if (!resetResult.Succeeded)
            return Problem(statusCode: StatusCodes.Status500InternalServerError);

        var roles = await _userManager.GetRolesAsync(user);
        if (!user.MustChangePassword &&
            (roles.Count != 1 || roles.Any(role => role is not ("Admin" or "Staff"))))
        {
            await WriteSystemAuditAsync(
                "Authentication.LoginDenied.InvalidRoleAssignment",
                user.Id,
                cancellationToken);
            return Forbid();
        }

        var issued = _tokenIssuer.Issue(user, roles.ToArray());
        await _auditWriter.WriteUserAsync(user.Id, "Authentication.LoginSucceeded", cancellationToken);

        return Ok(new LoginResponse(
            issued.Token,
            "Bearer",
            issued.ExpiresAt,
            user.MustChangePassword,
            user.FullName,
            roles.ToArray()));
    }

}
