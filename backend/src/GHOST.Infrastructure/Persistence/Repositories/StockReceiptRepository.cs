using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence.Repositories;

public sealed class StockReceiptRepository : IStockReceiptRepository
{
    private readonly GHOSTDbContext _db;

    public StockReceiptRepository(GHOSTDbContext db) => _db = db;

    public Task<StockReceipt?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.StockReceipts.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<StockReceipt?> GetByIdempotencyKeyAsync(Guid createdById, string key, CancellationToken cancellationToken = default)
        => _db.StockReceipts.Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.CreatedById == createdById && r.IdempotencyKey == key, cancellationToken);

    public async Task<IReadOnlyList<StockReceipt>> ListAsync(
        StockReceiptStatus? status,
        Guid? createdById,
        CancellationToken cancellationToken = default)
    {
        var query = _db.StockReceipts.AsNoTracking().Include(r => r.Items).AsQueryable();
        if (status is not null)
            query = query.Where(r => r.Status == status);
        if (createdById is not null)
            query = query.Where(r => r.CreatedById == createdById);

        return await query.OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(StockReceipt receipt, CancellationToken cancellationToken = default)
        => await _db.StockReceipts.AddAsync(receipt, cancellationToken);
}
