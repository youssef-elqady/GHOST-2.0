namespace GHOST.Application.Abstractions.Auditing;

/// <summary>
/// Records that a financially, stock or permission significant attempt was REFUSED. Written independently of the failed
/// operation (its own save), so the rejection survives even though the business change was rolled back or never started.
/// Never pass passwords, tokens or other secrets: only the action, the entity and our own refusal message.
/// </summary>
public interface IRejectionAudit
{
    Task RecordAsync(Guid? actorUserId, string action, string entityType, string? entityId, string reason, CancellationToken cancellationToken = default);
}

public static class RejectionAuditExtensions
{
    /// <summary>Null-safe helper so handlers built without the service (older tests) keep working.</summary>
    public static Task RejectedAsync(this IRejectionAudit? audit, Guid? actorUserId, string action, string entityType, Guid? entityId, string? reason, CancellationToken cancellationToken = default)
        => audit is null ? Task.CompletedTask : audit.RecordAsync(actorUserId, action, entityType, entityId?.ToString(), reason ?? "Rejected", cancellationToken);
}
