namespace GHOST.API.Authentication;

public interface IAuthenticationAuditWriter
{
    Task WriteSystemAsync(string action, Guid? targetUserId, CancellationToken cancellationToken);
    Task WriteUserAsync(Guid userId, string action, CancellationToken cancellationToken);
}
