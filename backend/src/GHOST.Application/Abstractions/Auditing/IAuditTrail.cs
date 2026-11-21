namespace GHOST.Application.Abstractions.Auditing;

/// <summary>
/// Stages an audit record in the current unit of work. The record is persisted by the same
/// SaveChanges as the business change, so the two commit or fail together.
/// </summary>
public interface IAuditTrail
{
    void Record(
        Guid actorUserId,
        string action,
        string entityType,
        string entityId,
        string? newValuesJson = null);
}
