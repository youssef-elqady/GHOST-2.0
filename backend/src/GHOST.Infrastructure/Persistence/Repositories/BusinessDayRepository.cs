
using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence.Repositories;

public sealed class BusinessDayRepository : IBusinessDayRepository
{
    private readonly GHOSTDbContext _dbContext;

    public BusinessDayRepository(GHOSTDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<BusinessDay?> GetByDateAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.BusinessDays
            .AsNoTracking()
            .FirstOrDefaultAsync(
                day => day.BusinessDate == businessDate,
                cancellationToken);
    }

    public Task<BusinessDay?> GetOpenAsync(
        CancellationToken cancellationToken = default)
    {
        return _dbContext.BusinessDays
            .AsNoTracking()
            .FirstOrDefaultAsync(
                day => !day.IsClosed,
                cancellationToken);
    }

    public async Task AddAsync(
        BusinessDay businessDay,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.BusinessDays.AddAsync(
            businessDay,
            cancellationToken);
    }
}