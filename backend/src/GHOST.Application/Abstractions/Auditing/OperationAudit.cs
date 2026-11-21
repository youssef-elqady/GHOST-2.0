using System.Text.Json;
using GHOST.Application.Abstractions.CurrentUser;

namespace GHOST.Application.Abstractions.Auditing;

/// <summary>
/// Stages an audit row for the signed-in user inside the current unit of work. Handlers receive the audit trail and the
/// current user as optional dependencies, so old call sites and tests keep working; in the running API both are present.
/// </summary>
public static class OperationAudit
{
    public static void Record(
        IAuditTrail? audit, ICurrentUser? user, string action, string entityType, Guid entityId, object? values = null)
    {
        if (audit is null || user?.UserId is not { } actor || actor == Guid.Empty)
            return;

        audit.Record(actor, action, entityType, entityId.ToString(), values is null ? null : JsonSerializer.Serialize(values));
    }
}
