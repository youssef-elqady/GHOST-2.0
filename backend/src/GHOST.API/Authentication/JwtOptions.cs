namespace GHOST.API.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";

    public string? Issuer { get; init; }

    public string? Audience { get; init; }

    public string? SigningKey { get; init; }

    public int AccessTokenLifetimeMinutes { get; init; }
}
