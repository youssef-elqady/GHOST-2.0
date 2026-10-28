using GHOST.Domain.Common;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

/// <summary>
/// A sellable catalog item. StockQuantity and AverageUnitCost are transactional
/// balances; StockMovements remain the audit trail used to explain those balances.
/// </summary>
public sealed class Product : BaseEntity
{
    public string Name { get; private set; } = null!;
    public string? Sku { get; private set; }
    public string Category { get; private set; } = null!;
    public string Unit { get; private set; } = null!;
    public decimal SellingPrice { get; private set; }
    public decimal AverageUnitCost { get; private set; }
    public decimal StockQuantity { get; private set; }
    public decimal MinimumStock { get; private set; }
    public bool IsActive { get; private set; }

    private Product()
    {
    }

    public Product(
        string name,
        string? sku,
        string category,
        string unit,
        decimal sellingPrice,
        decimal minimumStock)
    {
        Name = ValidateRequiredText(name, nameof(name), 150);
        Sku = NormalizeSku(sku);
        Category = ValidateRequiredText(category, nameof(category), 100);
        Unit = ValidateRequiredText(unit, nameof(unit), 30);
        ValidateMoney(sellingPrice, nameof(sellingPrice), allowZero: false);
        ValidateQuantity(minimumStock, nameof(minimumStock), allowZero: true);

        SellingPrice = sellingPrice;
        MinimumStock = minimumStock;
        AverageUnitCost = 0m;
        StockQuantity = 0m;
        IsActive = true;
    }

    public bool IsLowStock => StockQuantity <= MinimumStock;

    public void UpdateDetails(
        string name,
        string? sku,
        string category,
        string unit,
        decimal sellingPrice,
        decimal minimumStock)
    {
        Name = ValidateRequiredText(name, nameof(name), 150);
        Sku = NormalizeSku(sku);
        Category = ValidateRequiredText(category, nameof(category), 100);
        Unit = ValidateRequiredText(unit, nameof(unit), 30);
        ValidateMoney(sellingPrice, nameof(sellingPrice), allowZero: false);
        ValidateQuantity(minimumStock, nameof(minimumStock), allowZero: true);

        SellingPrice = sellingPrice;
        MinimumStock = minimumStock;
        MarkAsUpdated();
    }

    public void SetActive(bool isActive)
    {
        if (IsActive == isActive)
            return;

        IsActive = isActive;
        MarkAsUpdated();
    }

    /// <summary>
    /// Receives stock at a known unit cost and recalculates weighted average cost.
    /// Call this and append its StockMovement in the same database transaction.
    /// </summary>
    public void ReceiveStock(decimal quantity, decimal unitCost)
    {
        ValidateQuantity(quantity, nameof(quantity), allowZero: false);
        ValidateMoney(unitCost, nameof(unitCost), allowZero: true);

        var newQuantity = StockQuantity + quantity;
        var totalInventoryCost = (StockQuantity * AverageUnitCost) + (quantity * unitCost);

        AverageUnitCost = decimal.Round(
            totalInventoryCost / newQuantity,
            4,
            MidpointRounding.AwayFromZero);
        StockQuantity = newQuantity;
        MarkAsUpdated();
    }

    /// <summary>
    /// Removes stock using the current weighted-average cost. A caller must append
    /// a matching negative movement in the same database transaction.
    /// </summary>
    public decimal RemoveStock(decimal quantity)
    {
        ValidateQuantity(quantity, nameof(quantity), allowZero: false);

        if (quantity > StockQuantity)
            throw new BusinessRuleValidationException(
                $"Insufficient stock. Available: {StockQuantity:N3}; requested: {quantity:N3}.");

        var costAtRemoval = AverageUnitCost;
        StockQuantity -= quantity;
        MarkAsUpdated();
        return costAtRemoval;
    }

    private static string ValidateRequiredText(string? value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new BusinessRuleValidationException($"{parameterName} is required.");

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new BusinessRuleValidationException(
                $"{parameterName} cannot exceed {maxLength} characters.");

        return normalized;
    }

    private static string? NormalizeSku(string? sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return null;

        var normalized = sku.Trim().ToUpperInvariant();
        if (normalized.Length > 64)
            throw new BusinessRuleValidationException("SKU cannot exceed 64 characters.");

        return normalized;
    }

    private static void ValidateMoney(decimal value, string parameterName, bool allowZero)
    {
        if (value < 0 || (!allowZero && value == 0))
            throw new BusinessRuleValidationException(
                $"{parameterName} must be {(allowZero ? "zero or greater" : "greater than zero")}.");

        if (decimal.Round(value, 2) != value)
            throw new BusinessRuleValidationException($"{parameterName} can have at most two decimal places.");
    }

    private static void ValidateQuantity(decimal value, string parameterName, bool allowZero)
    {
        if (value < 0 || (!allowZero && value == 0))
            throw new BusinessRuleValidationException(
                $"{parameterName} must be {(allowZero ? "zero or greater" : "greater than zero")}.");

        if (decimal.Round(value, 3) != value)
            throw new BusinessRuleValidationException($"{parameterName} can have at most three decimal places.");
    }
}
