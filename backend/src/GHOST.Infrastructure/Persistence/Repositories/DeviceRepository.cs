using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence.Repositories;

public sealed class DeviceRepository : IDeviceRepository
{
    private readonly GHOSTDbContext _dbContext;

    public DeviceRepository(GHOSTDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Device>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Devices
            .AsNoTracking()
            .Include(device => device.Rates)
            .OrderBy(device => device.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Device?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Devices
            .Include(device => device.Rates)
            .SingleOrDefaultAsync(device => device.Id == id, cancellationToken);
    }

    public async Task AddAsync(
        Device device,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.Devices.AddAsync(device, cancellationToken);
    }
}
