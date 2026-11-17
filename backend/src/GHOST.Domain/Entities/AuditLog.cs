namespace GHOST.Domain.Entities;

/// <summary>
/// Immutable audit record. Only creation is exposed; database permissions and
/// a SQL Server trigger provide the authoritative append-only guarantee.
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; private set; }

    public Guid? UserId { get; private set; }

    public string ActorType { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string? EntityId { get; private set; }

    public string? OldValues { get; private set; }

    public string? NewValues { get; private set; }

    public DateTimeOffset At { get; private set; }

    private AuditLog()
    {
    }

    public static AuditLog Create(
        string actorType,
        Guid? userId,
        string action,
        string entityType,
        string? entityId = null,
        string? oldValues = null,
        string? newValues = null,
        DateTimeOffset? at = null)
    {
        if (actorType is not ("System" or "User"))
            throw new ArgumentOutOfRangeException(nameof(actorType), "Actor type must be System or User.");

        if ((actorType == "System" && userId is not null) ||
            (actorType == "User" && (userId is null || userId == Guid.Empty)))
        {
            throw new ArgumentException(
                "System actors must not have a user ID; user actors require a non-empty user ID.",
                nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(action) || action.Length > 100)
            throw new ArgumentException("Action is required and must not exceed 100 characters.", nameof(action));

        if (string.IsNullOrWhiteSpace(entityType) || entityType.Length > 100)
            throw new ArgumentException("Entity type is required and must not exceed 100 characters.", nameof(entityType));

        if (entityId?.Length > 128)
            throw new ArgumentException("Entity ID must not exceed 128 characters.", nameof(entityId));

        if (oldValues is not null && System.Text.Encoding.Unicode.GetByteCount(oldValues) > 16 * 1024)
            throw new ArgumentException("Old values must not exceed 16 KiB.", nameof(oldValues));

        if (newValues is not null && System.Text.Encoding.Unicode.GetByteCount(newValues) > 16 * 1024)
            throw new ArgumentException("New values must not exceed 16 KiB.", nameof(newValues));

        return new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorType = actorType,
            UserId = userId,
            Action = action.Trim(),
            EntityType = entityType.Trim(),
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
            At = (at ?? DateTimeOffset.UtcNow).ToUniversalTime()
        };
    }
}
