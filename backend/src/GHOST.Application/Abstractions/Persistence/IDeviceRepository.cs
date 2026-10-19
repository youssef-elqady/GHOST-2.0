using GHOST.Domain.Entities;

namespace GHOST.Application.Abstractions.Persistence;

public interface IDeviceRepository
{
    Task<IReadOnlyList<Device>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Device device,
        CancellationToken cancellationToken = default);
}
