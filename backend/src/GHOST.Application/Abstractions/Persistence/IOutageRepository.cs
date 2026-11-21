using GHOST.Domain.Entities;

namespace GHOST.Application.Abstractions.Persistence;

public interface IOutageRepository
{
    Task<DateTimeOffset?> GetHeartbeatAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the heartbeat immediately (its own save, no business change attached).</summary>
    Task SaveHeartbeatAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Session>> ListActiveSessionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Session>> ListNeedingReviewAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, string>> GetDeviceNamesAsync(
        IReadOnlyCollection<Guid> deviceIds, CancellationToken cancellationToken = default);
}
