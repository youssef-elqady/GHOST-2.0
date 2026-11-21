using System.IdentityModel.Tokens.Jwt;
using GHOST.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace GHOST.API.Authentication;

/// <summary>
/// Makes logout, replay revocation and account disable effective for access tokens that
/// were already issued: an access token is only accepted while its session family and its
/// user are still active in the database.
/// </summary>
public static class SessionTokenValidator
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null)
        {
            context.Fail("missing_principal");
            return;
        }

        // The restricted password-change token has no session family by design.
        if (principal.FindFirst("token_use")?.Value != JwtTokenService.AccessTokenPurpose)
            return;

        if (!Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId) ||
            !Guid.TryParse(principal.FindFirst(JwtTokenService.SessionIdClaim)?.Value, out var familyId))
        {
            context.Fail("invalid_session");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var dbContext = services.GetRequiredService<GHOSTDbContext>();
        var cancellationToken = context.HttpContext.RequestAborted;

        var userIsActive = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId && user.IsActive, cancellationToken);

        if (!userIsActive)
        {
            context.Fail("inactive_user");
            return;
        }

        var refreshTokens = services.GetRequiredService<RefreshTokenService>();
        if (!await refreshTokens.IsFamilyActiveAsync(userId, familyId, cancellationToken))
            context.Fail("revoked_session");
    }
}
