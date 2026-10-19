using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence.Repositories;

public sealed class SessionRepository : ISessionRepository
{
    private readonly GHOSTDbContext _dbContext;

    public SessionRepository(GHOSTDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Session?> GetByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Sessions
            .Include(session => session.Segments)
                .ThenInclude(segment => segment.Pauses)
            .FirstOrDefaultAsync(
                session => session.Id == sessionId,
                cancellationToken);
    }

    public Task<Session?> GetActiveByDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Sessions
            .Where(session => session.Status == SessionStatus.Active)
            .Where(session => session.Segments.Any(
                segment => segment.DeviceId == deviceId && segment.EndedAt == null))
            .Include(session => session.Segments)
                .ThenInclude(segment => segment.Pauses)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Device?> GetDeviceForStartAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Devices
            .Include(device => device.Rates)
            .FirstOrDefaultAsync(
                device => device.Id == deviceId,
                cancellationToken);
    }

    public Task<Shift?> GetShiftAsync(
        Guid shiftId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Shifts
            .FirstOrDefaultAsync(
                shift => shift.Id == shiftId,
                cancellationToken);
    }

    public async Task AddAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.Sessions.AddAsync(session, cancellationToken);
    }
}
