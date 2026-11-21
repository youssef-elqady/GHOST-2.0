using GHOST.Domain.Common;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

/// <summary>
/// Sellable catalog item (SRS 4.4, ERD 6.4). Stock quantity and weighted average cost are
/// transactional balances that can only move through <see cref="ApplyMovement"/>, which
/// also produces the append-only <see cref="StockMovement"/> that explains the change.
/// </summary>
public sealed class Product : BaseEntity
{
    public const decimal DefaultMinStock = 10m;
    public const decimal DefaultWarningStock = 30m;

    private readonly List<StockMovement> _movements = new();
    private readonly List<ProductPriceHistory> _priceHistory = new();

    public string Name { get; private set; } = string.Empty;

    public string? Barcode { get; private set; }

    public string? Sku { get; private set; }

    public string Category { get; private set; } = string.Empty;

    public string Unit { get; private set; } = string.Empty;

    /// <summary>Whole EGP only (BD-13). Zero while the product is PendingPricing.</summary>
    public decimal SalePrice { get; private set; }

    public decimal AvgCost { get; private set; }

    public decimal StockQuantity { get; private set; }

    /// <summary>Below this the stock is critical (SRS default: less than 10).</summary>
    public decimal MinStock { get; private set; }

    /// <summary>Below this the stock is low (SRS default: 10 to 30).</summary>
    public decimal WarningStock { get; private set; }

    public bool TrackStock { get; private set; }

    public ProductStatus Status { get; private set; }

    public Guid? CreatedById { get; private set; }

    public IReadOnlyCollection<StockMovement> Movements => _movements.AsReadOnly();

    public IReadOnlyCollection<ProductPriceHistory> PriceHistory => _priceHistory.AsReadOnly();

    private Product()
    {
    }

    /// <summary>Admin creates a sellable product with its price.</summary>
    public static Product CreateActive(
        string name,
        string? barcode,
        string? sku,
        string category,
        string unit,
        decimal salePrice,
        Guid createdById,
        decimal? minStock = null,
        decimal? warningStock = null,
        bool trackStock = true)
    {
        ValidateSalePrice(salePrice);
        var product = Build(name, barcode, sku, category, unit, createdById, minStock, warningStock, trackStock);
        product.SalePrice = salePrice;
        product.Status = ProductStatus.Active;
        return product;
    }

    /// <summary>Staff quick-add (FR-31, FR-36): no price, cannot be sold until an Admin prices it.</summary>
    public static Product CreatePendingPricing(
        string name,
        string? barcode,
        string category,
        string unit,
        Guid createdById,
        bool trackStock = true)
    {
        var product = Build(name, barcode, null, category, unit, createdById, null, null, trackStock);
        product.SalePrice = 0m;
        product.Status = ProductStatus.PendingPricing;
        return product;
    }

    public bool IsSellable => Status == ProductStatus.Active;

    public StockLevel StockLevel =>
        !TrackStock ? StockLevel.Untracked
        : StockQuantity < MinStock ? StockLevel.Critical
        : StockQuantity <= WarningStock ? StockLevel.Low
        : StockLevel.Good;

    /// <summary>Admin sets or changes the sale price and approves a pending product.</summary>
    public void SetSalePrice(decimal newPrice, Guid changedById, string? reason)
    {
        ValidateSalePrice(newPrice);

        if (Status == ProductStatus.Disabled)
            throw new BusinessRuleValidationException("A disabled product cannot be priced.");

        if (newPrice != SalePrice)
        {
            _priceHistory.Add(ProductPriceHistory.Create(
                Id, ProductPriceType.Sale, SalePrice, newPrice, changedById, reason));
            SalePrice = newPrice;
        }

        Status = ProductStatus.Active;
        MarkAsUpdated();
    }

    public void Disable()
    {
        Status = ProductStatus.Disabled;
        MarkAsUpdated();
    }

    public void Enable()
    {
        if (SalePrice <= 0)
            throw new BusinessRuleValidationException("Set a sale price before enabling this product.");

        Status = ProductStatus.Active;
        MarkAsUpdated();
    }

    public void SetStockThresholds(decimal minStock, decimal warningStock)
    {
        ValidateThresholds(minStock, warningStock);
        MinStock = minStock;
        WarningStock = warningStock;
        MarkAsUpdated();
    }

    /// <summary>
    /// The only way stock changes. Purchases at a known cost update the weighted average cost.
    /// Stock never goes below zero (constraint 9) and cost-less outflows use the current
    /// average cost. A reason is mandatory.
    /// </summary>
    public StockMovement ApplyMovement(
        StockMovementType type,
        decimal quantity,
        Guid userId,
        string? reason,
        Guid? shiftId = null,
        decimal? unitCost = null,
        string? sourceType = null,
        Guid? sourceId = null)
    {
        if (!TrackStock)
            throw new BusinessRuleValidationException("This product does not track stock.");

        if (quantity <= 0 || decimal.Round(quantity, 3) != quantity)
            throw new BusinessRuleValidationException(
                "Quantity must be greater than zero with at most three decimal places.");

        if (type == StockMovementType.Sale && !IsSellable)
            throw new BusinessRuleValidationException(
                Status == ProductStatus.PendingPricing
                    ? "This product is waiting for an Admin to set its price and cannot be sold yet."
                    : "A disabled product cannot be sold.");

        var direction = IsInbound(type) ? 1 : -1;
        var delta = direction * quantity;
        var newQuantity = StockQuantity + delta;

        if (newQuantity < 0)
            throw new BusinessRuleValidationException(
                $"Insufficient stock. Available: {StockQuantity:N3}; requested: {quantity:N3}.");

        decimal cost;
        if (type == StockMovementType.Purchase)
        {
            if (unitCost is null || unitCost < 0)
                throw new BusinessRuleValidationException("A purchase needs a unit cost of zero or more.");

            cost = unitCost.Value;
            var totalCost = (StockQuantity * AvgCost) + (quantity * cost);
            var oldAvg = AvgCost;
            AvgCost = decimal.Round(totalCost / newQuantity, 4, MidpointRounding.AwayFromZero);

            if (AvgCost != oldAvg)
                _priceHistory.Add(ProductPriceHistory.Create(
                    Id, ProductPriceType.Cost, oldAvg, AvgCost, userId, "Purchase"));
        }
        else
        {
            cost = unitCost ?? AvgCost;
        }

        var movement = StockMovement.Create(
            Id, type, delta, cost, newQuantity, shiftId, userId, sourceType, sourceId, reason);

        StockQuantity = newQuantity;
        _movements.Add(movement);
        MarkAsUpdated();
        return movement;
    }

    /// <summary>Gives a product its barcode once; a barcode is never silently replaced.</summary>
    public void AssignBarcode(string barcode)
    {
        if (!string.IsNullOrEmpty(Barcode))
            throw new BusinessRuleValidationException("This product already has a barcode.");

        Barcode = OptionalCode(barcode, "Barcode", 64)
            ?? throw new BusinessRuleValidationException("Barcode is required.");
        MarkAsUpdated();
    }

    /// <summary>
    /// Applies an approved stocktake difference (counted minus the system quantity at count time).
    /// The delta is signed, so surpluses add stock and shortages remove it. Never goes below zero.
    /// </summary>
    public StockMovement ApplyStocktakeAdjustment(
        decimal delta,
        Guid userId,
        string? reason,
        Guid sourceId)
    {
        if (!TrackStock)
            throw new BusinessRuleValidationException("This product does not track stock.");

        if (delta == 0 || decimal.Round(delta, 3) != delta)
            throw new BusinessRuleValidationException("The stocktake difference must be non-zero with at most three decimals.");

        var newQuantity = StockQuantity + delta;
        if (newQuantity < 0)
            throw new BusinessRuleValidationException(
                $"'{Name}': the adjustment would take stock below zero (current {StockQuantity:N3}, difference {delta:N3}).");

        var movement = StockMovement.Create(
            Id, StockMovementType.StocktakeAdjustment, delta, AvgCost, newQuantity, null, userId,
            nameof(Stocktake), sourceId, reason);

        StockQuantity = newQuantity;
        _movements.Add(movement);
        MarkAsUpdated();
        return movement;
    }

    public static bool IsInbound(StockMovementType type) => type is
        StockMovementType.Purchase or
        StockMovementType.SaleReturn;

    private static Product Build(
        string name,
        string? barcode,
        string? sku,
        string category,
        string unit,
        Guid createdById,
        decimal? minStock,
        decimal? warningStock,
        bool trackStock)
    {
        if (createdById == Guid.Empty)
            throw new BusinessRuleValidationException("The acting user is required.");

        var min = minStock ?? DefaultMinStock;
        var warning = warningStock ?? DefaultWarningStock;
        ValidateThresholds(min, warning);

        return new Product
        {
            Name = RequiredText(name, "Name", 150),
            Barcode = OptionalCode(barcode, "Barcode", 64),
            Sku = OptionalCode(sku, "SKU", 64),
            Category = RequiredText(category, "Category", 100),
            Unit = RequiredText(unit, "Unit", 30),
            MinStock = min,
            WarningStock = warning,
            TrackStock = trackStock,
            CreatedById = createdById
        };
    }

    private static void ValidateSalePrice(decimal price)
    {
        if (price <= 0)
            throw new BusinessRuleValidationException("Sale price must be greater than zero.");

        if (decimal.Floor(price) != price)
            throw new BusinessRuleValidationException("Sale price must be a whole number of EGP (BD-13).");
    }

    private static void ValidateThresholds(decimal min, decimal warning)
    {
        if (min < 0 || warning < min)
            throw new BusinessRuleValidationException(
                "Stock thresholds must satisfy 0 <= critical <= warning.");
    }

    private static string RequiredText(string? value, string field, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new BusinessRuleValidationException($"{field} is required.");

        var trimmed = value.Trim();
        if (trimmed.Length > max)
            throw new BusinessRuleValidationException($"{field} cannot exceed {max} characters.");

        return trimmed;
    }

    private static string? OptionalCode(string? value, string field, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length > max)
            throw new BusinessRuleValidationException($"{field} cannot exceed {max} characters.");

        return trimmed;
    }
}

public enum StockLevel
{
    Untracked = 0,
    Critical = 1,
    Low = 2,
    Good = 3
}
