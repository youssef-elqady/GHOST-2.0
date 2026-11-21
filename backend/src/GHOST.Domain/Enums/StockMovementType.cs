namespace GHOST.Domain.Enums;

/// <summary>Movement types from SRS ERD 6.4.</summary>
public enum StockMovementType
{
    Purchase = 1,
    Sale = 2,
    SaleReturn = 3,
    Waste = 4,
    Expired = 5,
    RecipeConsumption = 6,
    StocktakeAdjustment = 7,
    ManualAdjustment = 8,
    PurchaseReversal = 9,

    /// <summary>Reduces stock to fix an intake entered with too large a quantity. Always linked to the original movement.</summary>
    IntakeCorrection = 10
}
