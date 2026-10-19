using GHOST.Domain.Entities;

namespace GHOST.Application.Abstractions.Persistence;

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<Session?> GetActiveByDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Session session,
        CancellationToken cancellationToken = default);
}
