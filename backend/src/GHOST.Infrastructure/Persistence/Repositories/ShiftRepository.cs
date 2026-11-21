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
}
