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
    public const string AccessTokenPurpose = "access";
    public const string PasswordChangeTokenPurpose = "password_change";

    private static readonly TimeSpan PasswordChangeLifetime = TimeSpan.FromMinutes(5);
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public const string SessionIdClaim = "sid";

    public IssuedJwtToken CreateAccessToken(
        ApplicationUser user,
        IEnumerable<string> roles,
        Guid sessionFamilyId)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roles);

        if (sessionFamilyId == Guid.Empty)
            throw new ArgumentException("Session family ID is required.", nameof(sessionFamilyId));

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenLifetimeMinutes);
        var claims = CreateBaseClaims(user);
        claims.Add(new Claim("token_use", AccessTokenPurpose));
        claims.Add(new Claim(SessionIdClaim, sessionFamilyId.ToString("D")));

        foreach (var role in roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim("role", role));
        }

        return Issue(claims, now, expiresAt);
    }

    public IssuedJwtToken CreatePasswordChangeToken(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(PasswordChangeLifetime);
        var claims = CreateBaseClaims(user);
        claims.Add(new Claim("token_use", PasswordChangeTokenPurpose));

        return Issue(claims, now, expiresAt);
    }

    private static List<Claim> CreateBaseClaims(ApplicationUser user) =>
    [
        new(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
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
