using System.Text.Json;
using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Common.Result;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Application.Inventory;

public sealed record CreateProductCommand(
    Guid ActorUserId,
    bool ActorIsAdmin,
    string Name,
    string? Barcode,
    string? Sku,
    string Category,
    string Unit,
    decimal? SalePrice,
    decimal? MinStock,
    decimal? WarningStock,
    bool TrackStock);

public sealed record ProductDto(
    Guid Id,
    string Name,
    string? Barcode,
    string? Sku,
    string Category,
    string Unit,
    decimal SalePrice,
    decimal AvgCost,
    decimal StockQuantity,
    decimal MinStock,
    decimal WarningStock,
    string Status,
    string StockLevel,
    bool TrackStock);

public sealed record StockMovementDto(
    Guid Id,
    string Type,
    decimal QtyDelta,
    decimal UnitCost,
    decimal BalanceAfter,
    Guid? ShiftId,
    Guid UserId,
    string Reason,
    DateTimeOffset CreatedAt);

public sealed record AdjustStockCommand(
    Guid ProductId,
    Guid ActorUserId,
    Guid? ShiftId,
    StockMovementType Type,
    decimal Quantity,
    decimal? UnitCost,
    string? Reason);

/// <summary>
/// Product catalog and stock movements (SRS FR-30..36). Staff may quick-add a product
/// without a price (it stays PendingPricing); only an Admin can price or enable it.
/// Stock changes only through movements that record the actor and a reason.
/// </summary>
public sealed class InventoryHandler
{
    /// <summary>Movement types Staff may record; purchases need the receipt flow and Admin.</summary>
    public static readonly StockMovementType[] ManualTypes =
    {
        StockMovementType.Waste,
        StockMovementType.Expired,
        StockMovementType.ManualAdjustment
    };

    private readonly IProductRepository _products;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _audit;

    public InventoryHandler(IProductRepository products, IUnitOfWork unitOfWork, IAuditTrail audit)
    {
        _products = products;
        _unitOfWork = unitOfWork;
        _audit = audit;
    }

    public async Task<Result<ProductDto>> CreateProductAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(command.Barcode) &&
                await _products.BarcodeExistsAsync(command.Barcode.Trim(), cancellationToken))
            {
                return Result<ProductDto>.Failure("A product with this barcode already exists.");
            }

            Product product;
            if (command.ActorIsAdmin && command.SalePrice is not null)
            {
                product = Product.CreateActive(
                    command.Name, command.Barcode, command.Sku, command.Category, command.Unit,
                    command.SalePrice.Value, command.ActorUserId,
                    command.MinStock, command.WarningStock, command.TrackStock);
            }
            else
            {
                // Staff (or an Admin without a price) cannot create a sellable product.
                product = Product.CreatePendingPricing(
                    command.Name, command.Barcode, command.Category, command.Unit,
                    command.ActorUserId, command.TrackStock);
            }

            await _products.AddAsync(product, cancellationToken);
            _audit.Record(
                command.ActorUserId,
                "Product.Created",
                "Product",
                product.Id.ToString(),
                JsonSerializer.Serialize(new { product.Name, product.Barcode, status = product.Status.ToString() }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<ProductDto>.Success(ToDto(product));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<ProductDto>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<ProductDto>.Failure(exception.Message);
        }
    }

    public async Task<Result<ProductDto>> SetPriceAsync(
        Guid productId,
        Guid actorUserId,
        decimal price,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var product = await _products.GetAsync(productId, cancellationToken);
        if (product is null)
            return Result<ProductDto>.Failure("Product was not found.");

        try
        {
            var old = product.SalePrice;
            product.SetSalePrice(price, actorUserId, reason);
            _audit.Record(
                actorUserId,
                "Product.PriceSet",
                "Product",
                product.Id.ToString(),
                JsonSerializer.Serialize(new { old, @new = price }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<ProductDto>.Success(ToDto(product));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<ProductDto>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<ProductDto>.Failure(exception.Message);
        }
    }

    public async Task<Result<ProductDto>> SetStatusAsync(
        Guid productId,
        Guid actorUserId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var product = await _products.GetAsync(productId, cancellationToken);
        if (product is null)
            return Result<ProductDto>.Failure("Product was not found.");

        try
        {
            if (enabled)
                product.Enable();
            else
                product.Disable();

            _audit.Record(
                actorUserId,
                enabled ? "Product.Enabled" : "Product.Disabled",
                "Product",
                product.Id.ToString(),
                null);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<ProductDto>.Success(ToDto(product));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<ProductDto>.Failure(exception.Message);
        }
    }

    public async Task<Result<StockMovementDto>> AdjustStockAsync(
        AdjustStockCommand command,
        bool actorIsAdmin,
        CancellationToken cancellationToken = default)
    {
        if (!actorIsAdmin && !ManualTypes.Contains(command.Type))
            return Result<StockMovementDto>.Failure("Only an Admin can record this movement type.");

        if (command.Type is StockMovementType.Sale or StockMovementType.SaleReturn
            or StockMovementType.RecipeConsumption or StockMovementType.StocktakeAdjustment)
        {
            return Result<StockMovementDto>.Failure(
                "This movement type is created by its own workflow, not manually.");
        }

        var product = await _products.GetAsync(command.ProductId, cancellationToken);
        if (product is null)
            return Result<StockMovementDto>.Failure("Product was not found.");

        try
        {
            var movement = product.ApplyMovement(
                command.Type, command.Quantity, command.ActorUserId, command.Reason,
                command.ShiftId, command.UnitCost);

            _audit.Record(
                command.ActorUserId,
                "Stock.Adjusted",
                "Product",
                product.Id.ToString(),
                JsonSerializer.Serialize(new { type = command.Type.ToString(), movement.QtyDelta, movement.Reason }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<StockMovementDto>.Success(ToDto(movement));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<StockMovementDto>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<StockMovementDto>.Failure(exception.Message);
        }
    }

    public async Task<IReadOnlyList<ProductDto>> ListAsync(
        ProductStatus? status,
        string? search,
        CancellationToken cancellationToken = default)
        => (await _products.ListAsync(status, search, cancellationToken)).Select(ToDto).ToList();

    public async Task<Result<ProductDto>> GetByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken = default)
    {
        var product = await _products.GetByBarcodeAsync(barcode.Trim(), cancellationToken);
        return product is null
            ? Result<ProductDto>.Failure("Product was not found.")
            : Result<ProductDto>.Success(ToDto(product));
    }

    public async Task<IReadOnlyList<StockMovementDto>> GetMovementsAsync(
        Guid productId,
        int take,
        CancellationToken cancellationToken = default)
        => (await _products.GetMovementsAsync(productId, take, cancellationToken)).Select(ToDto).ToList();

    public static ProductDto ToDto(Product p) => new(
        p.Id, p.Name, p.Barcode, p.Sku, p.Category, p.Unit, p.SalePrice, p.AvgCost,
        p.StockQuantity, p.MinStock, p.WarningStock, p.Status.ToString(), p.StockLevel.ToString(),
        p.TrackStock);

    private static StockMovementDto ToDto(StockMovement m) => new(
        m.Id, m.Type.ToString(), m.QtyDelta, m.UnitCost, m.BalanceAfter, m.ShiftId, m.UserId,
        m.Reason, m.CreatedAt);
}
