namespace GHOST.API.Authentication;

/// <summary>
/// GHOST runs on one venue PC: the browser UI and the API are both on this machine. Allowed origins come from
/// configuration (Authentication:RefreshToken:AllowedOrigins) and must be exact loopback origins. A wildcard,
/// a LAN address or a public host fails startup instead of silently opening the API to the network.
/// </summary>
public static class LocalOriginPolicy
{
    public static string[] Validate(IEnumerable<string>? origins)
    {
        var list = (origins ?? []).Select(o => o?.Trim() ?? string.Empty).Where(o => o.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (list.Length == 0)
            throw new InvalidOperationException("Authentication:RefreshToken:AllowedOrigins must list at least one local frontend origin.");

        foreach (var origin in list)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
                || origin.EndsWith('/'))
                throw new InvalidOperationException($"Allowed origin '{origin}' is not an exact origin such as http://localhost:3000.");

            if (!(uri.IsLoopback || uri.Host is "localhost"))
                throw new InvalidOperationException($"Allowed origin '{origin}' is not a loopback address. GHOST is local-only.");
        }

        return list;
    }
}
