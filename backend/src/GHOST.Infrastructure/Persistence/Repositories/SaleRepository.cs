using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence.Repositories;

public sealed class SaleRepository : ISaleRepository
{
    public const string InvoiceSequence = "InvoiceNumbers";

    private readonly GHOSTDbContext _db;

    public SaleRepository(GHOSTDbContext db) => _db = db;

    private IQueryable<Sale> WithChildren() => _db.Sales
        .Include(s => s.Items)
        .Include(s => s.Payments)
        .Include(s => s.Refunds)
        .AsSplitQuery();

    public Task<Sale?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => WithChildren().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<Sale?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default)
        => WithChildren().FirstOrDefaultAsync(s => s.IdempotencyKey == key, cancellationToken);

    public Task<bool> SessionIsInvoicedAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => _db.Sales.AnyAsync(
            s => s.SessionId == sessionId && s.Status != SaleStatus.Refunded && s.Status != SaleStatus.Voided,
            cancellationToken);

    public async Task<Guid?> GetReplaceableSessionInvoiceIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => await _db.Sales.AsNoTracking()
            .Where(s => s.SessionId == sessionId && (s.Status == SaleStatus.Refunded || s.Status == SaleStatus.Voided))
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<string> NextInvoiceNumberAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var next = await _db.Database
            .SqlQuery<long>($"SELECT NEXT VALUE FOR [dbo].[InvoiceNumbers] AS [Value]")
            .SingleAsync(cancellationToken);

        return $"GS-{now.Year}-{next:D5}";
    }

    public async Task<IReadOnlyList<Sale>> ListAsync(
        Guid? shiftId,
        SaleStatus? status,
        Guid? createdById,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Sales.AsNoTracking().Include(s => s.Items).Include(s => s.Payments).Include(s => s.Refunds).AsSplitQuery().AsQueryable();
        if (shiftId is not null) query = query.Where(s => s.ShiftId == shiftId);
        if (status is not null) query = query.Where(s => s.Status == status);
        if (createdById is not null) query = query.Where(s => s.CreatedById == createdById);

        return await query.OrderByDescending(s => s.CreatedAt).Take(200).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Sale sale, CancellationToken cancellationToken = default)
        => await _db.Sales.AddAsync(sale, cancellationToken);
}
