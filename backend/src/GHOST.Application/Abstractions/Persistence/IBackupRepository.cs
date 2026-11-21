using GHOST.Domain.Entities;

namespace GHOST.Application.Abstractions.Persistence;

public interface IBackupRepository
{
    Task AddAsync(BackupRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupRecord>> ListAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>Succeeded, not yet pruned backups, newest first (tracked, so they can be marked pruned).</summary>
    Task<IReadOnlyList<BackupRecord>> ListLiveAsync(CancellationToken cancellationToken = default);
}
