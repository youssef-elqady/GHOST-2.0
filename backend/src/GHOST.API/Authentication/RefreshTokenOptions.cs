namespace GHOST.API.Authentication;

public sealed record RefreshTokenOptions
{
    public const string SectionName = "Authentication:RefreshToken";
    public const string CookieName = "ghost_refresh";
    public const string CookiePath = "/api/auth";
    public const string RequestHeaderName = "X-GHOST-Request";
    public const string RequestHeaderValue = "1";

    /// <summary>Sliding lifetime of a single refresh token.</summary>
    public int SlidingLifetimeDays { get; init; } = 7;

    /// <summary>Absolute lifetime of a session family, counted from the initial login.</summary>
    public int AbsoluteLifetimeDays { get; init; } = 30;

    /// <summary>Exact browser origins allowed to call refresh/logout.</summary>
    public string[] AllowedOrigins { get; init; } =
    [
        "http://localhost:3000",
        "http://127.0.0.1:3000"
    ];
}
