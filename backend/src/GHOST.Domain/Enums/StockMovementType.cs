namespace GHOST.Domain.Enums;

/// <summary>
/// Describes why inventory changed. Movements are append-only audit records.
/// </summary>
public enum StockMovementType
{
    Receipt = 1,
    Sale = 2,
    CustomerReturn = 3,
    Waste = 4,
    AdjustmentIncrease = 5,
    AdjustmentDecrease = 6
}
