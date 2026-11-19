using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GHOST.API.Authentication;
using GHOST.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GHOST.Tests.Identity;

public sealed class JwtAccessTokenIssuerTests
{
    private const string SigningKey = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Issue_CreatesSignedShortLivedTokenWithUserAndRoleClaims()
    {
        var user = new ApplicationUser
        {
            FullName = "GHOST Admin",
            MustChangePassword = false
        };
        var issuer = CreateIssuer();

        var issued = issuer.Issue(user, ["Admin"]);
        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(
            issued.Token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "GHOST",
                ValidateAudience = true,
                ValidAudience = "GHOST.StaffAdmin",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                NameClaimType = GhostClaimTypes.Name,
                RoleClaimType = GhostClaimTypes.Role
            },
            out var validatedToken);

        Assert.Equal(user.Id.ToString(), principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal("GHOST Admin", principal.FindFirst(GhostClaimTypes.Name)?.Value);
        Assert.True(principal.IsInRole("Admin"));
        Assert.Equal("false", principal.FindFirst(GhostClaimTypes.MustChangePassword)?.Value);
        Assert.DoesNotContain(principal.Claims, claim => claim.Type == GhostClaimTypes.BusinessAccess);
        Assert.IsType<JwtSecurityToken>(validatedToken);
        Assert.InRange(
            issued.ExpiresAt - DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(9),
            TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Issue_ForPasswordChangeAccountOmitsBusinessRoles()
    {
        var user = new ApplicationUser
        {
            FullName = "New Staff",
            MustChangePassword = true
        };
        var issuer = CreateIssuer();

        var issued = issuer.Issue(user, ["Staff"]);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);

        Assert.Equal("true", jwt.Claims.Single(
            claim => claim.Type == GhostClaimTypes.MustChangePassword).Value);
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type == GhostClaimTypes.Role);
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type == GhostClaimTypes.BusinessAccess);
    }

    private static JwtAccessTokenIssuer CreateIssuer()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "GHOST",
            Audience = "GHOST.StaffAdmin",
            SigningKey = SigningKey,
            AccessTokenLifetimeMinutes = 10
        });

        return new JwtAccessTokenIssuer(options, TimeProvider.System);
    }
}
