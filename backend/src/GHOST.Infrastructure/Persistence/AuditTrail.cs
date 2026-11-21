using GHOST.Application.Abstractions.Auditing;
using GHOST.Domain.Entities;

namespace GHOST.Infrastructure.Persistence;

public sealed class AuditTrail : IAuditTrail
{
    private readonly GHOSTDbContext _dbContext;

    public AuditTrail(GHOSTDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Record(
        Guid actorUserId,
        string action,
        string entityType,
        string entityId,
        string? newValuesJson = null)
    {
        _dbContext.AuditLogs.Add(AuditLog.Create(
            actorType: "User",
            userId: actorUserId,
            action: action,
            entityType: entityType,
            entityId: entityId,
            newValues: newValuesJson));
    }
}

public sealed class SystemAuditTrail : GHOST.Application.Abstractions.Auditing.ISystemAuditTrail
{
    private readonly GHOSTDbContext _dbContext;

    public SystemAuditTrail(GHOSTDbContext dbContext) => _dbContext = dbContext;

    public void Record(string action, string entityType, string entityId, string? newValuesJson = null)
        => _dbContext.AuditLogs.Add(AuditLog.Create(
            actorType: "System",
            userId: null,
            action: action,
            entityType: entityType,
            entityId: entityId,
            newValues: newValuesJson));
}
