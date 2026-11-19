namespace GHOST.Application.Identity.Bootstrap;

public enum AdminBootstrapStatus
{
    Succeeded,
    InvalidRequest,
    AdminAlreadyExists,
    IdentityRejected,
    RoleConflict
}

public sealed record AdminBootstrapResult(
    AdminBootstrapStatus Status,
    Guid? UserId = null,
    IReadOnlyList<string>? ErrorCodes = null);
