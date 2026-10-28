using GHOST.Domain.Entities;

namespace GHOST.Application.Abstractions.Persistence;

/// <summary>
/// Application-facing contract for catalog and inventory operations.
/// Stock changes are implemented transactionally by Infrastructure.
/// </summary>
public interface IInventoryService
{
    Task<IReadOnlyList<Product>> GetProductsAsync(
        CancellationToken cancellationToken = default);

    Task<Product?> GetProductAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<Product> CreateProductAsync(
        Product product,
        CancellationToken cancellationToken = default);

    Task<Product?> UpdateProductAsync(
        Guid productId,
        string name,
        string? sku,
        string category,
        string unit,
        decimal sellingPrice,
        decimal minimumStock,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<StockMovement?> ReceiveStockAsync(
        Guid productId,
        decimal quantity,
        decimal unitCost,
        string? reference,
        string? notes,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockMovement>> GetStockMovementsAsync(
        Guid productId,
        CancellationToken cancellationToken = default);
}
