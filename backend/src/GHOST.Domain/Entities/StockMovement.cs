using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

/// <summary>
/// Append-only inventory record (FR-33). Quantities change only through these rows; the
/// database blocks UPDATE and DELETE. Created exclusively by <see cref="Product"/>.
/// </summary>
public sealed class StockMovement
{
    public const int MaxReasonLength = 500;

    public Guid Id { get; private set; }

    public Guid ProductId { get; private set; }

    public StockMovementType Type { get; private set; }

    /// <summary>Positive for stock in, negative for stock out.</summary>
    public decimal QtyDelta { get; private set; }

    public decimal UnitCost { get; private set; }

    public decimal BalanceAfter { get; private set; }

    public Guid? ShiftId { get; private set; }

    public Guid UserId { get; private set; }

    public string? SourceType { get; private set; }

    public Guid? SourceId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    private StockMovement()
    {
    }

    internal static StockMovement Create(
        Guid productId,
        StockMovementType type,
        decimal qtyDelta,
        decimal unitCost,
        decimal balanceAfter,
        Guid? shiftId,
        Guid userId,
        string? sourceType,
        Guid? sourceId,
        string? reason)
    {
        if (productId == Guid.Empty)
            throw new BusinessRuleValidationException("ProductId cannot be empty.");

        if (userId == Guid.Empty)
            throw new BusinessRuleValidationException("The acting user is required.");

        if (!Enum.IsDefined(type))
            throw new BusinessRuleValidationException("Stock movement type is invalid.");

        if (qtyDelta == 0 || decimal.Round(qtyDelta, 3) != qtyDelta)
            throw new BusinessRuleValidationException(
                "Quantity must be non-zero with at most three decimal places.");

        if (unitCost < 0)
            throw new BusinessRuleValidationException("Unit cost cannot be negative.");

        var trimmed = reason?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new BusinessRuleValidationException("A reason is required for every stock movement.");

        if (trimmed.Length > MaxReasonLength)
            throw new BusinessRuleValidationException($"Reason cannot exceed {MaxReasonLength} characters.");

        return new StockMovement
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Type = type,
            QtyDelta = qtyDelta,
            UnitCost = decimal.Round(unitCost, 4),
            BalanceAfter = balanceAfter,
            ShiftId = shiftId,
            UserId = userId,
            SourceType = sourceType,
            SourceId = sourceId,
            Reason = trimmed,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
