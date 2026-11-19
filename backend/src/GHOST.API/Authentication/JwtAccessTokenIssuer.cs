using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GHOST.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GHOST.API.Authentication;

public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;

    public JwtAccessTokenIssuer(
        IOptions<JwtOptions> options,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public IssuedAccessToken Issue(
        ApplicationUser user,
        IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roles);

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.AccessTokenLifetimeMinutes);
        var mustChangePassword = user.MustChangePassword;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat,
                now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
            new(GhostClaimTypes.Name, user.FullName),
            new(GhostClaimTypes.MustChangePassword,
                mustChangePassword ? "true" : "false")
        };

        // A temporary-password token deliberately carries no business role.
        // Authorization policies also block it from normal endpoints.
        if (!mustChangePassword)
        {
            claims.AddRange(roles.Select(role => new Claim(GhostClaimTypes.Role, role)));
        }

        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_options.SigningKey!));
        var credentials = new SigningCredentials(
            signingKey,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new IssuedAccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }
}
