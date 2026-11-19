using GHOST.Domain.Entities;
using GHOST.Infrastructure.Persistence;

namespace GHOST.API.Authentication;

public sealed class AuthenticationAuditWriter : IAuthenticationAuditWriter
{
    private readonly GHOSTDbContext _dbContext;

    public AuthenticationAuditWriter(GHOSTDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task WriteSystemAsync(
        string action,
        Guid? targetUserId,
        CancellationToken cancellationToken)
    {
        _dbContext.AuditLogs.Add(AuditLog.Create(
            actorType: "System",
            userId: null,
            action: action,
            entityType: "Authentication",
            entityId: targetUserId?.ToString()));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task WriteUserAsync(
        Guid userId,
        string action,
        CancellationToken cancellationToken)
    {
        _dbContext.AuditLogs.Add(AuditLog.Create(
            actorType: "User",
            userId: userId,
            action: action,
            entityType: "User",
            entityId: userId.ToString()));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
