using GHOST.Domain.Entities;

namespace GHOST.Application.Abstractions.Persistence;

public interface ISettingsRepository
{
    Task<IReadOnlyList<SystemSetting>> ListAsync(CancellationToken cancellationToken = default);

    Task<SystemSetting?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task AddAsync(SystemSetting setting, CancellationToken cancellationToken = default);
}
