using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence.Repositories;

public sealed class ProductRepository : IProductRepository
{
    private readonly GHOSTDbContext _db;

    public ProductRepository(GHOSTDbContext db) => _db = db;

    public Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
        => _db.Products.FirstOrDefaultAsync(p => p.Barcode == barcode, cancellationToken);

    public Task<bool> BarcodeExistsAsync(string barcode, CancellationToken cancellationToken = default)
        => _db.Products.AnyAsync(p => p.Barcode == barcode, cancellationToken);

    public async Task<IReadOnlyList<Product>> ListAsync(
        ProductStatus? status,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Products.AsNoTracking().AsQueryable();

        if (status is not null)
            query = query.Where(p => p.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Barcode == term || p.Sku == term);
        }

        return await query.OrderBy(p => p.Name).Take(500).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockMovement>> GetMovementsAsync(
        Guid productId,
        int take,
        CancellationToken cancellationToken = default)
    {
        return await _db.StockMovements
            .AsNoTracking()
            .Where(m => m.ProductId == productId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
        => await _db.Products.AddAsync(product, cancellationToken);
}
