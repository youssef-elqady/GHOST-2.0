using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence.Repositories;

public sealed class ShiftRepository : IShiftRepository
{
    private readonly GHOSTDbContext _dbContext;

    public ShiftRepository(GHOSTDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Shift?> GetWithCashAsync(
        Guid shiftId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Shifts
            .Include(shift => shift.CashTransactions)
            .FirstOrDefaultAsync(shift => shift.Id == shiftId, cancellationToken);
    }

    public Task<Shift?> GetOpenShiftForUserWithCashAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Shifts
            .Include(shift => shift.CashTransactions)
            .Where(shift => shift.StaffUserId == userId && shift.Status == GHOST.Domain.Enums.ShiftStatus.Open)
            .OrderByDescending(shift => shift.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Shift?> GetAsync(
        Guid shiftId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Shifts.FirstOrDefaultAsync(shift => shift.Id == shiftId, cancellationToken);
    }

    public Task<bool> HandoverExistsAsync(
        Guid fromShiftId,
        Guid toShiftId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.ShiftHandovers.AnyAsync(
            handover => handover.FromShiftId == fromShiftId || handover.ToShiftId == toShiftId,
            cancellationToken);
    }

    public async Task AddHandoverAsync(
        ShiftHandover handover,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.ShiftHandovers.AddAsync(handover, cancellationToken);
    }
}
