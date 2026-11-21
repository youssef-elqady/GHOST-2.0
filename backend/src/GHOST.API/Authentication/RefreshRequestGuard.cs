using Microsoft.AspNetCore.Http;

namespace GHOST.API.Authentication;

/// <summary>
/// CSRF defence for cookie-authenticated endpoints: exact Origin allowlist plus a custom
/// header that forces a CORS preflight for cross-origin callers.
/// </summary>
public static class RefreshRequestGuard
{
    public static bool IsTrusted(HttpRequest request, RefreshTokenOptions options)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin) ||
            !options.AllowedOrigins.Contains(origin, StringComparer.Ordinal))
            return false;

        return request.Headers[RefreshTokenOptions.RequestHeaderName].ToString()
            == RefreshTokenOptions.RequestHeaderValue;
    }

    public static CookieOptions CreateCookieOptions(
        bool isDevelopment,
        DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Path = RefreshTokenOptions.CookiePath,
        Secure = !isDevelopment,
        Expires = expiresAt,
        IsEssential = true
    };
}
