using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Tests.Inventory;

public sealed class ProductTests
{
    private static readonly Guid User = Guid.NewGuid();

    private static Product Active() =>
        Product.CreateActive("Cola", "6221", null, "Drinks", "can", 20m, User);

    [Fact]
    public void StaffQuickAdd_IsPendingAndCannotBeSold()
    {
        var p = Product.CreatePendingPricing("Chips", null, "Snacks", "bag", User);
        p.ApplyMovement(StockMovementType.Purchase, 10, User, "initial", unitCost: 5m);

        Assert.False(p.IsSellable);
        Assert.Throws<BusinessRuleValidationException>(() =>
            p.ApplyMovement(StockMovementType.Sale, 1, User, "sale"));
    }

    [Fact]
    public void SetSalePrice_ApprovesPendingProduct_AndRecordsHistory()
    {
        var p = Product.CreatePendingPricing("Chips", null, "Snacks", "bag", User);
        p.SetSalePrice(15m, User, "approved");

        Assert.True(p.IsSellable);
        Assert.Single(p.PriceHistory);
    }

    [Fact]
    public void SalePrice_MustBeWholeEgp()
    {
        Assert.Throws<BusinessRuleValidationException>(() =>
            Product.CreateActive("X", null, null, "c", "u", 12.5m, User));
    }

    [Fact]
    public void Purchases_UpdateWeightedAverageCost()
    {
        var p = Active();
        p.ApplyMovement(StockMovementType.Purchase, 10, User, "r1", unitCost: 10m);
        p.ApplyMovement(StockMovementType.Purchase, 10, User, "r2", unitCost: 20m);

        Assert.Equal(20m, p.StockQuantity);
        Assert.Equal(15m, p.AvgCost);
    }

    [Fact]
    public void Stock_CannotGoNegative()
    {
        var p = Active();
        p.ApplyMovement(StockMovementType.Purchase, 2, User, "r", unitCost: 1m);

        Assert.Throws<BusinessRuleValidationException>(() =>
            p.ApplyMovement(StockMovementType.Waste, 3, User, "broken"));
        Assert.Equal(2m, p.StockQuantity);
    }

    [Fact]
    public void Movement_RequiresReason_AndRecordsBalance()
    {
        var p = Active();
        Assert.Throws<BusinessRuleValidationException>(() =>
            p.ApplyMovement(StockMovementType.Purchase, 2, User, " ", unitCost: 1m));

        var m = p.ApplyMovement(StockMovementType.Purchase, 5, User, "receipt", unitCost: 2m);
        p.ApplyMovement(StockMovementType.Waste, 2, User, "spilled");

        Assert.Equal(5m, m.BalanceAfter);
        Assert.Equal(3m, p.StockQuantity);
        Assert.Equal(-2m, p.Movements.Last().QtyDelta);
    }

    [Fact]
    public void StockLevel_FollowsThresholds()
    {
        var p = Active();
        p.ApplyMovement(StockMovementType.Purchase, 5, User, "r", unitCost: 1m);
        Assert.Equal(StockLevel.Critical, p.StockLevel);
        p.ApplyMovement(StockMovementType.Purchase, 20, User, "r", unitCost: 1m);
        Assert.Equal(StockLevel.Low, p.StockLevel);
        p.ApplyMovement(StockMovementType.Purchase, 20, User, "r", unitCost: 1m);
        Assert.Equal(StockLevel.Good, p.StockLevel);
    }
}
