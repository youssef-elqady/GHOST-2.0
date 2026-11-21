using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence.Repositories;

public sealed class SettingsRepository : ISettingsRepository
{
    private readonly GHOSTDbContext _db;

    public SettingsRepository(GHOSTDbContext db) => _db = db;

    public async Task<IReadOnlyList<SystemSetting>> ListAsync(CancellationToken cancellationToken = default)
        => await _db.SystemSettings.AsNoTracking().ToListAsync(cancellationToken);

    public Task<SystemSetting?> GetAsync(string key, CancellationToken cancellationToken = default)
        => _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

    public async Task AddAsync(SystemSetting setting, CancellationToken cancellationToken = default)
        => await _db.SystemSettings.AddAsync(setting, cancellationToken);
}

public sealed class BackupRepository : IBackupRepository
{
    private readonly GHOSTDbContext _db;

    public BackupRepository(GHOSTDbContext db) => _db = db;

    public async Task AddAsync(BackupRecord record, CancellationToken cancellationToken = default)
        => await _db.BackupRecords.AddAsync(record, cancellationToken);

    public async Task<IReadOnlyList<BackupRecord>> ListAsync(int take, CancellationToken cancellationToken = default)
        => await _db.BackupRecords.AsNoTracking().OrderByDescending(b => b.CreatedAt).Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BackupRecord>> ListLiveAsync(CancellationToken cancellationToken = default)
        => await _db.BackupRecords.Where(b => b.Status == BackupStatus.Succeeded && b.PrunedAt == null)
            .OrderByDescending(b => b.CreatedAt).ToListAsync(cancellationToken);
}
