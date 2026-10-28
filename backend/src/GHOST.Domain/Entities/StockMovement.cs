using GHOST.Domain.Common;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

/// <summary>
/// Append-only inventory audit record. QuantityDelta is positive for stock-in
/// and negative for stock-out; BalanceAfter is the balance after this movement.
/// </summary>
public sealed class StockMovement : BaseEntity
{
    public Guid ProductId { get; private set; }
    public StockMovementType Type { get; private set; }
    public decimal QuantityDelta { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal BalanceAfter { get; private set; }
    public string? Reference { get; private set; }
    public string? Notes { get; private set; }

    private StockMovement()
    {
    }

    public StockMovement(
        Guid productId,
        StockMovementType type,
        decimal quantityDelta,
        decimal unitCost,
        decimal balanceAfter,
        string? reference = null,
        string? notes = null)
    {
        if (productId == Guid.Empty)
            throw new BusinessRuleValidationException("ProductId cannot be empty.");
        if (!Enum.IsDefined(type))
            throw new BusinessRuleValidationException("Stock movement type is invalid.");
        if (quantityDelta == 0 || decimal.Round(quantityDelta, 3) != quantityDelta)
            throw new BusinessRuleValidationException(
                "QuantityDelta must be non-zero and have at most three decimal places.");
        if (unitCost < 0 || decimal.Round(unitCost, 2) != unitCost)
            throw new BusinessRuleValidationException(
                "UnitCost must be zero or greater and have at most two decimal places.");
        if (balanceAfter < 0 || decimal.Round(balanceAfter, 3) != balanceAfter)
            throw new BusinessRuleValidationException(
                "BalanceAfter must be zero or greater and have at most three decimal places.");

        if (reference?.Length > 100)
            throw new BusinessRuleValidationException("Reference cannot exceed 100 characters.");
        if (notes?.Length > 500)
            throw new BusinessRuleValidationException("Notes cannot exceed 500 characters.");

        ProductId = productId;
        Type = type;
        QuantityDelta = quantityDelta;
        UnitCost = unitCost;
        BalanceAfter = balanceAfter;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
