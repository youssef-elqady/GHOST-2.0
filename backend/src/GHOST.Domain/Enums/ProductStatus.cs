namespace GHOST.Domain.Enums;

public enum ProductStatus
{
    Active = 1,
    /// <summary>Added by Staff; cannot be sold until an Admin sets the price (FR-36).</summary>
    PendingPricing = 2,
    Disabled = 3
}
