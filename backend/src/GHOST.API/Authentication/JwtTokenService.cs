using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GHOST.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GHOST.API.Authentication;

public sealed record IssuedJwtToken(string Value, DateTimeOffset ExpiresAtUtc);

public sealed class JwtTokenService
{
    private static readonly TimeSpan PasswordChangeLifetime = TimeSpan.FromMinutes(5);
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public IssuedJwtToken CreateAccessToken(
        ApplicationUser user,
        IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roles);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenLifetimeMinutes);
        var claims = CreateBaseClaims(user);
        claims.Add(new Claim("token_use", "access"));

        foreach (var role in roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return Issue(claims, now, expiresAt);
    }

    public IssuedJwtToken CreatePasswordChangeToken(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(PasswordChangeLifetime);
        var claims = CreateBaseClaims(user);
        claims.Add(new Claim("token_use", "password_change"));

        return Issue(claims, now, expiresAt);
    }

    private static List<Claim> CreateBaseClaims(ApplicationUser user) =>
    [
        new(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
        new(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        new(ClaimTypes.Name, user.UserName ?? string.Empty),
        new("full_name", user.FullName)
    ];

    private IssuedJwtToken Issue(
        IEnumerable<Claim> claims,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_options.SigningKey!));
        var credentials = new SigningCredentials(
            signingKey,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new IssuedJwtToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }
}
