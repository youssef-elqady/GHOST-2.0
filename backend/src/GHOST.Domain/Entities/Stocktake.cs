using GHOST.Domain.Common;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

public enum StocktakeStatus
{
    Open = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4
}

public sealed class StocktakeItem
{
    public Guid Id { get; private set; }
    public Guid StocktakeId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;

    /// <summary>System quantity when the count started. Hidden from Staff until the count is submitted.</summary>
    public decimal SystemQuantity { get; private set; }

    public decimal? CountedQuantity { get; private set; }

    public decimal? Difference => CountedQuantity is null ? null : CountedQuantity.Value - SystemQuantity;

    private StocktakeItem()
    {
    }

    internal StocktakeItem(Guid stocktakeId, Product product)
    {
        Id = Guid.NewGuid();
        StocktakeId = stocktakeId;
        ProductId = product.Id;
        ProductName = product.Name;
        SystemQuantity = product.StockQuantity;
    }

    internal void Count(decimal quantity)
    {
        if (quantity < 0 || decimal.Round(quantity, 3) != quantity)
            throw new BusinessRuleValidationException(
                "Counted quantity must be zero or more with at most three decimal places.");

        CountedQuantity = quantity;
    }
}

/// <summary>
/// Blind physical count (SRS FR-38). Staff count without seeing the system quantity; after submit the
/// shortages and surpluses appear and an Admin approves the adjustment or rejects it.
/// </summary>
public sealed class Stocktake : BaseEntity
{
    private readonly List<StocktakeItem> _items = new();

    public Guid StartedById { get; private set; }
    public StocktakeStatus Status { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public Guid? DecidedById { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionNote { get; private set; }

    public IReadOnlyCollection<StocktakeItem> Items => _items.AsReadOnly();

    private Stocktake()
    {
    }

    public static Stocktake Start(Guid startedById, IReadOnlyList<Product> products)
    {
        if (startedById == Guid.Empty)
            throw new BusinessRuleValidationException("The acting user is required.");

        var tracked = products.Where(p => p.TrackStock).ToList();
        if (tracked.Count == 0)
            throw new BusinessRuleValidationException("There are no stock-tracked products to count.");

        var stocktake = new Stocktake { StartedById = startedById, Status = StocktakeStatus.Open };
        foreach (var product in tracked)
            stocktake._items.Add(new StocktakeItem(stocktake.Id, product));

        return stocktake;
    }

    /// <summary>Records counts. Products can be re-counted until the stocktake is submitted.</summary>
    public void RecordCounts(IReadOnlyDictionary<Guid, decimal> counts)
    {
        EnsureStatus(StocktakeStatus.Open, "Counts can only be changed while the stocktake is open.");

        if (counts.Count == 0)
            throw new BusinessRuleValidationException("Send at least one count.");

        foreach (var (productId, quantity) in counts)
        {
            var item = _items.FirstOrDefault(i => i.ProductId == productId)
                ?? throw new BusinessRuleValidationException("A counted product is not part of this stocktake.");
            item.Count(quantity);
        }

        MarkAsUpdated();
    }

    /// <summary>Submitting needs every product counted; nothing is assumed to be zero.</summary>
    public void Submit()
    {
        EnsureStatus(StocktakeStatus.Open, "This stocktake was already submitted.");

        var missing = _items.Count(i => i.CountedQuantity is null);
        if (missing > 0)
            throw new BusinessRuleValidationException($"{missing} product(s) have not been counted yet.");

        Status = StocktakeStatus.Submitted;
        SubmittedAt = DateTimeOffset.UtcNow;
        MarkAsUpdated();
    }

    /// <summary>Applies every non-zero difference as a stocktake adjustment movement.</summary>
    public void Approve(Guid adminId, string? note, IReadOnlyDictionary<Guid, Product> products)
    {
        EnsureStatus(StocktakeStatus.Submitted, "Only a submitted stocktake can be approved.");

        foreach (var item in _items.Where(i => i.Difference is not (null or 0)))
        {
            if (!products.TryGetValue(item.ProductId, out var product))
                throw new BusinessRuleValidationException("A counted product was not found.");

            product.ApplyStocktakeAdjustment(
                item.Difference!.Value,
                adminId,
                $"Stocktake: counted {item.CountedQuantity:N3}, system {item.SystemQuantity:N3}",
                Id);
        }

        Status = StocktakeStatus.Approved;
        DecidedById = adminId;
        DecidedAt = DateTimeOffset.UtcNow;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        MarkAsUpdated();
    }

    public void Reject(Guid adminId, string reason)
    {
        EnsureStatus(StocktakeStatus.Submitted, "Only a submitted stocktake can be rejected.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleValidationException("A rejection reason is required.");

        Status = StocktakeStatus.Rejected;
        DecidedById = adminId;
        DecidedAt = DateTimeOffset.UtcNow;
        DecisionNote = reason.Trim();
        MarkAsUpdated();
    }

    private void EnsureStatus(StocktakeStatus expected, string message)
    {
        if (Status != expected)
            throw new BusinessRuleValidationException(message);
    }
}
