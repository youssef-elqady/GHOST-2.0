namespace GHOST.API.Configuration;

/// <summary>
/// GHOST runs on one PC. The API may only listen on a loopback address. A wildcard (*, +, 0.0.0.0, [::]) or a LAN
/// address in Urls / ASPNETCORE_URLS fails startup, so the API cannot be exposed to the network by a config mistake.
/// </summary>
public static class LocalBindingPolicy
{
    public static void Validate(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
            return; // ASP.NET Core's default binding is localhost only.

        foreach (var raw in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = raw.Replace("://*", "://wildcard").Replace("://+", "://wildcard");
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
                throw new InvalidOperationException($"The listen address '{raw}' is not valid.");

            if (uri.Host == "wildcard" || !(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (System.Net.IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && System.Net.IPAddress.IsLoopback(ip))))
                throw new InvalidOperationException($"The listen address '{raw}' is not a loopback address. GHOST is local-only.");
        }
    }
}
