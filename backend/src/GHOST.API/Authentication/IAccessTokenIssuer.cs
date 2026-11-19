using GHOST.Infrastructure.Identity;

namespace GHOST.API.Authentication;

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(ApplicationUser user, IReadOnlyCollection<string> roles);
}

public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);
