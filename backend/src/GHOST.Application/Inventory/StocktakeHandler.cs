using System.Text.Json;
using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Common.Result;
using GHOST.Domain.Entities;
using GHOST.Domain.Exceptions;
using GHOST.Domain.Services;

namespace GHOST.Application.Inventory;

public sealed record StocktakeItemDto(
    Guid ProductId,
    string ProductName,
    decimal? CountedQuantity,
    decimal? SystemQuantity,
    decimal? Difference);

public sealed record StocktakeDto(
    Guid Id,
    string Status,
    Guid StartedById,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    string? DecisionNote,
    int ItemCount,
    int CountedCount,
    IReadOnlyList<StocktakeItemDto> Items);

/// <summary>Blind stocktake workflow (SRS FR-38) and internal barcode generation.</summary>
public sealed class StocktakeHandler
{
    private readonly IStocktakeRepository _stocktakes;
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _audit;

    public StocktakeHandler(
        IStocktakeRepository stocktakes,
        IProductRepository products,
        IUnitOfWork unitOfWork,
        IAuditTrail audit)
    {
        _stocktakes = stocktakes;
        _products = products;
        _unitOfWork = unitOfWork;
        _audit = audit;
    }

    public async Task<Result<StocktakeDto>> StartAsync(
        Guid actorUserId,
        bool actorIsAdmin,
        CancellationToken cancellationToken = default)
    {
        if (await _stocktakes.OpenStocktakeExistsAsync(cancellationToken))
            return Result<StocktakeDto>.Failure("A stocktake is already in progress. Finish or reject it first.");

        try
        {
            var products = await _products.ListAsync(null, null, cancellationToken);
            var stocktake = Stocktake.Start(actorUserId, products);
            await _stocktakes.AddAsync(stocktake, cancellationToken);

            _audit.Record(actorUserId, "Stocktake.Started", "Stocktake", stocktake.Id.ToString(),
                JsonSerializer.Serialize(new { items = stocktake.Items.Count }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<StocktakeDto>.Success(ToDto(stocktake, actorIsAdmin));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<StocktakeDto>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<StocktakeDto>.Failure(exception.Message);
        }
    }

    public async Task<Result<StocktakeDto>> RecordCountsAsync(
        Guid stocktakeId,
        Guid actorUserId,
        bool actorIsAdmin,
        IReadOnlyDictionary<Guid, decimal> counts,
        CancellationToken cancellationToken = default)
    {
        return await MutateAsync(stocktakeId, actorIsAdmin, "Stocktake.Counted", actorUserId,
            s => s.RecordCounts(counts), cancellationToken);
    }

    public async Task<Result<StocktakeDto>> SubmitAsync(
        Guid stocktakeId,
        Guid actorUserId,
        bool actorIsAdmin,
        CancellationToken cancellationToken = default)
    {
        return await MutateAsync(stocktakeId, actorIsAdmin, "Stocktake.Submitted", actorUserId,
            s => s.Submit(), cancellationToken);
    }

    public async Task<Result<StocktakeDto>> RejectAsync(
        Guid stocktakeId,
        Guid adminUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        return await MutateAsync(stocktakeId, true, "Stocktake.Rejected", adminUserId,
            s => s.Reject(adminUserId, reason), cancellationToken);
    }

    public async Task<Result<StocktakeDto>> ApproveAsync(
        Guid stocktakeId,
        Guid adminUserId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var stocktake = await _stocktakes.GetAsync(stocktakeId, cancellationToken);
        if (stocktake is null)
            return Result<StocktakeDto>.Failure("Stocktake was not found.");

        var ids = stocktake.Items.Select(i => i.ProductId).ToList();
        var products = (await _products.GetManyAsync(ids, cancellationToken)).ToDictionary(p => p.Id);

        try
        {
            stocktake.Approve(adminUserId, note, products);

            _audit.Record(adminUserId, "Stocktake.Approved", "Stocktake", stocktake.Id.ToString(),
                JsonSerializer.Serialize(new
                {
                    adjusted = stocktake.Items.Count(i => i.Difference is not (null or 0)),
                    shortage = stocktake.Items.Where(i => i.Difference < 0).Sum(i => i.Difference),
                    surplus = stocktake.Items.Where(i => i.Difference > 0).Sum(i => i.Difference)
                }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<StocktakeDto>.Success(ToDto(stocktake, true));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<StocktakeDto>.Failure(exception.Message);
        }
        catch (PersistenceConflictException)
        {
            return Result<StocktakeDto>.Failure("Stock changed while approving. Please try again.");
        }
    }

    public async Task<Result<StocktakeDto>> GetAsync(
        Guid stocktakeId,
        bool actorIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var stocktake = await _stocktakes.GetAsync(stocktakeId, cancellationToken);
        return stocktake is null
            ? Result<StocktakeDto>.Failure("Stocktake was not found.")
            : Result<StocktakeDto>.Success(ToDto(stocktake, actorIsAdmin));
    }

    public async Task<IReadOnlyList<StocktakeDto>> ListAsync(
        StocktakeStatus? status,
        bool actorIsAdmin,
        CancellationToken cancellationToken = default)
        => (await _stocktakes.ListAsync(status, cancellationToken)).Select(s => ToDto(s, actorIsAdmin)).ToList();

    /// <summary>Gives a product without a barcode a unique internal EAN-13.</summary>
    public async Task<Result<ProductDto>> GenerateBarcodeAsync(
        Guid productId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var product = await _products.GetAsync(productId, cancellationToken);
        if (product is null)
            return Result<ProductDto>.Failure("Product was not found.");

        if (!string.IsNullOrEmpty(product.Barcode))
            return Result<ProductDto>.Failure("This product already has a barcode.");

        try
        {
            // The sequence can only skip numbers, never repeat; retry if a skipped value collides.
            string code;
            var attempts = 0;
            do
            {
                code = Ean13.FromSequence(await _stocktakes.NextInternalBarcodeNumberAsync(cancellationToken));
            }
            while (await _products.BarcodeExistsAsync(code, cancellationToken) && ++attempts < 5);

            product.AssignBarcode(code);
            _audit.Record(actorUserId, "Product.BarcodeGenerated", "Product", product.Id.ToString(),
                JsonSerializer.Serialize(new { code }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<ProductDto>.Success(InventoryHandler.ToDto(product));
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

    private async Task<Result<StocktakeDto>> MutateAsync(
        Guid stocktakeId,
        bool actorIsAdmin,
        string auditAction,
        Guid actorUserId,
        Action<Stocktake> mutation,
        CancellationToken cancellationToken)
    {
        var stocktake = await _stocktakes.GetAsync(stocktakeId, cancellationToken);
        if (stocktake is null)
            return Result<StocktakeDto>.Failure("Stocktake was not found.");

        try
        {
            mutation(stocktake);
            _audit.Record(actorUserId, auditAction, "Stocktake", stocktake.Id.ToString(), null);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<StocktakeDto>.Success(ToDto(stocktake, actorIsAdmin));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<StocktakeDto>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<StocktakeDto>.Failure(exception.Message);
        }
    }

    /// <summary>
    /// Staff never see system quantities or differences while the count is open (blind count).
    /// After submission they see them, so the shortage or surplus is visible to everyone.
    /// </summary>
    private static StocktakeDto ToDto(Stocktake s, bool actorIsAdmin)
    {
        var reveal = actorIsAdmin || s.Status != StocktakeStatus.Open;
        var items = s.Items
            .OrderBy(i => i.ProductName)
            .Select(i => new StocktakeItemDto(
                i.ProductId,
                i.ProductName,
                i.CountedQuantity,
                reveal ? i.SystemQuantity : null,
                reveal ? i.Difference : null))
            .ToList();

        return new StocktakeDto(
            s.Id, s.Status.ToString(), s.StartedById, s.CreatedAt, s.SubmittedAt, s.DecisionNote,
            items.Count, items.Count(i => i.CountedQuantity is not null), items);
    }
}
