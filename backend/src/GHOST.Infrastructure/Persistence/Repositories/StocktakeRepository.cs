using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence.Repositories;

public sealed class StocktakeRepository : IStocktakeRepository
{
    private readonly GHOSTDbContext _db;

    public StocktakeRepository(GHOSTDbContext db) => _db = db;

    public Task<Stocktake?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.Stocktakes.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<bool> OpenStocktakeExistsAsync(CancellationToken cancellationToken = default)
        => _db.Stocktakes.AnyAsync(
            s => s.Status == StocktakeStatus.Open || s.Status == StocktakeStatus.Submitted, cancellationToken);

    public async Task<IReadOnlyList<Stocktake>> ListAsync(StocktakeStatus? status, CancellationToken cancellationToken = default)
    {
        var query = _db.Stocktakes.AsNoTracking().Include(s => s.Items).AsQueryable();
        if (status is not null)
            query = query.Where(s => s.Status == status);

        return await query.OrderByDescending(s => s.CreatedAt).Take(100).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Stocktake stocktake, CancellationToken cancellationToken = default)
        => await _db.Stocktakes.AddAsync(stocktake, cancellationToken);

    public Task<long> NextInternalBarcodeNumberAsync(CancellationToken cancellationToken = default)
        => _db.Database
            .SqlQuery<long>($"SELECT NEXT VALUE FOR [dbo].[InternalBarcodes] AS [Value]")
            .SingleAsync(cancellationToken);
}
