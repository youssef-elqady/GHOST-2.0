using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence.Repositories;

public sealed class OutageRepository : IOutageRepository
{
    private readonly GHOSTDbContext _db;

    public OutageRepository(GHOSTDbContext db) => _db = db;

    public async Task<DateTimeOffset?> GetHeartbeatAsync(CancellationToken cancellationToken = default)
        => await _db.SystemHeartbeats.AsNoTracking()
            .Where(h => h.Id == SystemHeartbeat.SingletonId)
            .Select(h => (DateTimeOffset?)h.BeatAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task SaveHeartbeatAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        // One UPDATE; insert only the first time. No tracked entity, so it never drags other pending changes along.
        var updated = await _db.SystemHeartbeats
            .Where(h => h.Id == SystemHeartbeat.SingletonId && h.BeatAt < now)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.BeatAt, now), cancellationToken);

        if (updated > 0)
            return;

        if (await _db.SystemHeartbeats.AnyAsync(h => h.Id == SystemHeartbeat.SingletonId, cancellationToken))
            return;

        _db.SystemHeartbeats.Add(new SystemHeartbeat(now));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race with another writer inserting the singleton row; the next beat updates it.
            _db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<Session>> ListActiveSessionsAsync(CancellationToken cancellationToken = default)
        => await _db.Sessions
            .Where(s => s.Status == SessionStatus.Active)
            .Include(s => s.Segments).ThenInclude(seg => seg.Pauses)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Session>> ListNeedingReviewAsync(CancellationToken cancellationToken = default)
        => await _db.Sessions
            .Where(s => s.NeedsReview && s.Status == SessionStatus.Active)
            .Include(s => s.Segments).ThenInclude(seg => seg.Pauses)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDeviceNamesAsync(
        IReadOnlyCollection<Guid> deviceIds, CancellationToken cancellationToken = default)
        => await _db.Devices.AsNoTracking()
            .Where(d => deviceIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
}
