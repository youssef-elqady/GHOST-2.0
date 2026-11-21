using GHOST.Domain.Entities;
using GHOST.Domain.Enums;

namespace GHOST.Application.Abstractions.Persistence;

public interface IStockReceiptRepository
{
    Task<StockReceipt?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockReceipt>> ListAsync(
        StockReceiptStatus? status,
        Guid? createdById,
        CancellationToken cancellationToken = default);

    Task AddAsync(StockReceipt receipt, CancellationToken cancellationToken = default);
}
