using System.IdentityModel.Tokens.Jwt;
using GHOST.Application.Abstractions.CurrentUser;

namespace GHOST.API.Security;

/// <summary>
/// Actor identity comes only from the validated JWT claims, never from request bodies.
/// </summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private System.Security.Claims.ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId =>
        IsAuthenticated &&
        Guid.TryParse(Principal!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) &&
        id != Guid.Empty
            ? id
            : null;

    public bool IsAdmin => IsAuthenticated && Principal!.IsInRole(AuthorizationPolicies.AdminRole);

    public bool IsStaff => IsAuthenticated && Principal!.IsInRole(AuthorizationPolicies.StaffRole);
}
