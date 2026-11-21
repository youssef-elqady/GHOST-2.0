using GHOST.Domain.Entities;
using GHOST.Domain.Enums;

namespace GHOST.Application.Abstractions.Persistence;

public interface ISaleRepository
{
    Task<Sale?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Sale?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>True when an invoice (not refunded) already covers this session.</summary>
    Task<bool> SessionIsInvoicedAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>The refunded or voided invoice that last covered this session, if any (a new invoice is then a Rebill).</summary>
    Task<Guid?> GetReplaceableSessionInvoiceIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult<Guid?>(null);

    /// <summary>Next gap-tolerant invoice number from the database sequence, e.g. GS-2026-00001.</summary>
    Task<string> NextInvoiceNumberAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Sale>> ListAsync(
        Guid? shiftId,
        SaleStatus? status,
        Guid? createdById,
        CancellationToken cancellationToken = default);

    Task AddAsync(Sale sale, CancellationToken cancellationToken = default);
}
