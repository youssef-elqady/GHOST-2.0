namespace GHOST.Application.Abstractions.Auditing;

/// <summary>Stages an audit record written by the system itself (no user), for example outage recovery at start-up.</summary>
public interface ISystemAuditTrail
{
    void Record(string action, string entityType, string entityId, string? newValuesJson = null);
}
