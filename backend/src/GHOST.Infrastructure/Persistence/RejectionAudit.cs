using System.Text.Json;
using GHOST.Application.Abstractions.Auditing;
using GHOST.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GHOST.Infrastructure.Persistence;

/// <summary>Writes the refusal row in its own scope and save, so a dirty failed operation can never be saved with it.</summary>
public sealed class RejectionAudit : IRejectionAudit
{
    private const int MaxReason = 300;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RejectionAudit> _logger;

    public RejectionAudit(IServiceScopeFactory scopes, ILogger<RejectionAudit> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task RecordAsync(Guid? actorUserId, string action, string entityType, string? entityId, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var clipped = reason.Length > MaxReason ? reason[..MaxReason] : reason;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GHOSTDbContext>();
            var hasUser = actorUserId is { } id && id != Guid.Empty;
            db.AuditLogs.Add(AuditLog.Create(
                actorType: hasUser ? "User" : "System",
                userId: hasUser ? actorUserId : null,
                action: action.Length > 100 ? action[..100] : action,
                entityType: entityType,
                entityId: entityId,
                newValues: JsonSerializer.Serialize(new { result = "Rejected", reason = clipped })));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // An audit failure must never turn a refusal into a crash; it is logged for the technician.
            _logger.LogError(exception, "Could not record rejected attempt {Action}", action);
        }
    }
}
