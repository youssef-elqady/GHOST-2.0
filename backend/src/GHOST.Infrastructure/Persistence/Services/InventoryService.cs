using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence.Services;

public sealed class InventoryService : IInventoryService
{
    private readonly GHOSTDbContext _dbContext;

    public InventoryService(GHOSTDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Product>> GetProductsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Product?> GetProductAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);
    }

    public async Task<Product> CreateProductAsync(
        Product product,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.Products.AddAsync(product, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return product;
    }

    public async Task<Product?> UpdateProductAsync(
        Guid productId,
        string name,
        string? sku,
        string category,
        string unit,
        decimal sellingPrice,
        decimal minimumStock,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products
            .SingleOrDefaultAsync(item => item.Id == productId, cancellationToken);

        if (product is null)
            return null;

        product.UpdateDetails(name, sku, category, unit, sellingPrice, minimumStock);
        product.SetActive(isActive);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return product;
    }

    public async Task<StockMovement?> ReceiveStockAsync(
        Guid productId,
        decimal quantity,
        decimal unitCost,
        string? reference,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        var product = await _dbContext.Products
            .SingleOrDefaultAsync(item => item.Id == productId, cancellationToken);

        if (product is null)
            return null;

        product.ReceiveStock(quantity, unitCost);

        var movement = new StockMovement(
            product.Id,
            StockMovementType.Receipt,
            quantity,
            unitCost,
            product.StockQuantity,
            reference,
            notes);

        await _dbContext.StockMovements.AddAsync(movement, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return movement;
    }

    public async Task<IReadOnlyList<StockMovement>> GetStockMovementsAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.StockMovements
            .AsNoTracking()
            .Where(movement => movement.ProductId == productId)
            .OrderByDescending(movement => movement.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
