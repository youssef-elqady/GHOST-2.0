using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

public enum ProductPriceType
{
    Sale = 1,
    Cost = 2
}

/// <summary>History of sale and cost price changes (ERD 6.4). Append-only by construction.</summary>
public sealed class ProductPriceHistory
{
    public Guid Id { get; private set; }

    public Guid ProductId { get; private set; }

    public ProductPriceType PriceType { get; private set; }

    public decimal OldValue { get; private set; }

    public decimal NewValue { get; private set; }

    public Guid ChangedById { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public string? Reason { get; private set; }

    private ProductPriceHistory()
    {
    }

    internal static ProductPriceHistory Create(
        Guid productId,
        ProductPriceType type,
        decimal oldValue,
        decimal newValue,
        Guid changedById,
        string? reason)
    {
        if (changedById == Guid.Empty)
            throw new BusinessRuleValidationException("The acting user is required.");

        return new ProductPriceHistory
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            PriceType = type,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedById = changedById,
            ChangedAt = DateTimeOffset.UtcNow,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
        };
    }
}
