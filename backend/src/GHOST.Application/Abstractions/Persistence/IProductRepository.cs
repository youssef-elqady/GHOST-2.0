using GHOST.Domain.Entities;
using GHOST.Domain.Enums;

namespace GHOST.Application.Abstractions.Persistence;

public interface IProductRepository
{
    Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);

    Task<bool> BarcodeExistsAsync(string barcode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Product>> ListAsync(
        ProductStatus? status,
        string? search,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockMovement>> GetMovementsAsync(
        Guid productId,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Product>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task AddAsync(Product product, CancellationToken cancellationToken = default);
}
