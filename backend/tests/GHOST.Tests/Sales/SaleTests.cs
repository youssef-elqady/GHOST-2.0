using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Tests.Sales;

public sealed class SaleTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private static Shift OpenShift(decimal opening = 100m)
        => new BusinessDay(new DateOnly(2026, 11, 21)).OpenShift(Staff, opening);

    private static Product Stocked(string name, decimal price, decimal qty, decimal cost)
    {
        var p = Product.CreateActive(name, null, null, "Drinks", "can", price, Admin);
        p.ApplyMovement(StockMovementType.Purchase, qty, Admin, "seed", unitCost: cost);
        return p;
    }

    private static Sale.ProductLine Line(Product p, decimal qty) => new(p, qty);

    [Fact]
    public void Checkout_DecrementsStock_AddsCash_FreezesPriceAndCost()
    {
        var shift = OpenShift();
        var cola = Stocked("Cola", 20m, 10, 12m);

        var sale = Sale.CheckoutCash(shift, Staff, "GS-2026-00001", new[] { Line(cola, 3) }, null, null, "k1");

        Assert.Equal(SaleStatus.Paid, sale.Status);
        Assert.Equal(60m, sale.Total);
        Assert.Equal(7m, cola.StockQuantity);
        Assert.Equal(160m, shift.ExpectedCash);
        var item = sale.Items.Single();
        Assert.Equal(20m, item.UnitPrice);
        Assert.Equal(12m, item.UnitCostSnapshot);
        Assert.Single(sale.Payments);
    }

    [Fact]
    public void Checkout_CannotSellMoreThanAvailable_AndChangesNothing()
    {
        var shift = OpenShift();
        var cola = Stocked("Cola", 20m, 2, 12m);

        Assert.Throws<BusinessRuleValidationException>(() =>
            Sale.CheckoutCash(shift, Staff, "GS-1", new[] { Line(cola, 3) }, null, null, null));
        Assert.Equal(2m, cola.StockQuantity);
        Assert.Equal(100m, shift.ExpectedCash);
    }

    [Fact]
    public void Checkout_RejectsPendingPricingProduct()
    {
        var shift = OpenShift();
        var pending = Product.CreatePendingPricing("Chips", null, "Snacks", "bag", Staff);

        Assert.Throws<BusinessRuleValidationException>(() =>
            Sale.CheckoutCash(shift, Staff, "GS-1", new[] { Line(pending, 1) }, null, null, null));
    }

    [Fact]
    public void Checkout_RequiresItems()
    {
        Assert.Throws<BusinessRuleValidationException>(() =>
            Sale.CheckoutCash(OpenShift(), Staff, "GS-1", Array.Empty<Sale.ProductLine>(), null, null, null));
    }

    [Fact]
    public void Discount_FixedAndPercent_ReduceTotalToWholeAmount()
    {
        var cola = Stocked("Cola", 25m, 20, 10m);
        var fixedSale = Sale.CheckoutCash(OpenShift(), Staff, "GS-1", new[] { Line(cola, 2) }, null,
            new Sale.DiscountInput(DiscountType.Fixed, 5m, "regular", Admin), null);
        Assert.Equal(45m, fixedSale.Total);

        var percent = Sale.CheckoutCash(OpenShift(), Staff, "GS-2", new[] { Line(cola, 3) }, null,
            new Sale.DiscountInput(DiscountType.Percent, 10m, "promo", Admin), null);
        // 75 * 10% = 7.5, rounded down to 7, total 68.
        Assert.Equal(7m, percent.DiscountAmount);
        Assert.Equal(68m, percent.Total);
    }

    [Fact]
    public void Discount_NeedsReason_AndCannotExceedSubtotal()
    {
        var cola = Stocked("Cola", 25m, 20, 10m);
        Assert.Throws<BusinessRuleValidationException>(() =>
            Sale.CheckoutCash(OpenShift(), Staff, "GS-1", new[] { Line(cola, 1) }, null,
                new Sale.DiscountInput(DiscountType.Fixed, 5m, " ", Admin), null));
        Assert.Throws<BusinessRuleValidationException>(() =>
            Sale.CheckoutCash(OpenShift(), Staff, "GS-2", new[] { Line(cola, 1) }, null,
                new Sale.DiscountInput(DiscountType.Fixed, 30m, "x", Admin), null));
    }

    [Fact]
    public void Checkout_RejectsFractionalTotal()
    {
        var kg = Stocked("Nuts", 15m, 10, 5m);
        Assert.Throws<BusinessRuleValidationException>(() =>
            Sale.CheckoutCash(OpenShift(), Staff, "GS-1", new[] { Line(kg, 0.5m) }, null, null, null));
    }

    [Fact]
    public void Gaming_OnlyCompletedSessions_AreInvoiced()
    {
        var session = new Session(Guid.NewGuid(), Guid.NewGuid(), PlayType.Single, 30m);
        Assert.Throws<BusinessRuleValidationException>(() =>
            Sale.CheckoutCash(OpenShift(), Staff, "GS-1", Array.Empty<Sale.ProductLine>(),
                new Sale.GamingLine(session, "PS5"), null, null));
    }

    [Fact]
    public void RefundAll_ReturnsStockAndCash_AndCannotRepeat()
    {
        var shift = OpenShift();
        var cola = Stocked("Cola", 20m, 10, 12m);
        var sale = Sale.CheckoutCash(shift, Staff, "GS-1", new[] { Line(cola, 3) }, null, null, null);
        var products = new Dictionary<Guid, Product> { [cola.Id] = cola };

        sale.RefundAll(Admin, "customer returned", true, shift, products);

        Assert.Equal(SaleStatus.Refunded, sale.Status);
        Assert.Equal(10m, cola.StockQuantity);
        Assert.Equal(100m, shift.ExpectedCash);
        Assert.Throws<BusinessRuleValidationException>(() =>
            sale.RefundAll(Admin, "again", true, shift, products));
    }

    [Fact]
    public void Refund_AfterOriginalShiftClosed_PostsCashOnTheOpenShift_AndKeepsInvoiceReference()
    {
        var shiftA = OpenShift();
        var cola = Stocked("Cola", 20m, 10, 12m);
        var sale = Sale.CheckoutCash(shiftA, Staff, "GS-1", new[] { Line(cola, 3) }, null, null, null);
        shiftA.Close(shiftA.ExpectedCash, null);
        var shiftB = new BusinessDay(new DateOnly(2026, 11, 22)).OpenShift(Guid.NewGuid(), 500m);
        var products = new Dictionary<Guid, Product> { [cola.Id] = cola };

        sale.RefundAll(Admin, "customer returned next day", true, shiftB, products);

        Assert.Equal(440m, shiftB.ExpectedCash);
        Assert.Equal(160m, shiftA.ExpectedCash);
        var refundRow = shiftB.CashTransactions.Single(t => t.Type == CashTransactionType.Refund);
        Assert.Equal(sale.Id, refundRow.SourceId);
        Assert.Equal(shiftA.Id, sale.ShiftId);
    }

    [Fact]
    public void Refund_IsBlockedWhenTheGivenShiftIsClosed_AndChangesNothing()
    {
        var shiftA = OpenShift();
        var cola = Stocked("Cola", 20m, 10, 12m);
        var sale = Sale.CheckoutCash(shiftA, Staff, "GS-1", new[] { Line(cola, 3) }, null, null, null);
        shiftA.Close(shiftA.ExpectedCash, null);
        var products = new Dictionary<Guid, Product> { [cola.Id] = cola };

        Assert.Throws<BusinessRuleValidationException>(() => sale.RefundAll(Admin, "late", true, shiftA, products));
        Assert.Equal(SaleStatus.Paid, sale.Status);
        Assert.Equal(7m, cola.StockQuantity);
    }

    [Fact]
    public void RefundAll_WithoutReturnToStock_KeepsStockDown()
    {
        var shift = OpenShift();
        var cola = Stocked("Cola", 20m, 10, 12m);
        var sale = Sale.CheckoutCash(shift, Staff, "GS-1", new[] { Line(cola, 3) }, null, null, null);

        sale.RefundAll(Admin, "spoiled", false, shift, new Dictionary<Guid, Product> { [cola.Id] = cola });

        Assert.Equal(7m, cola.StockQuantity);
        Assert.Equal(100m, shift.ExpectedCash);
    }

    [Fact]
    public void Print_FirstIsOriginal_LaterAreCopies()
    {
        var cola = Stocked("Cola", 20m, 10, 12m);
        var sale = Sale.CheckoutCash(OpenShift(), Staff, "GS-1", new[] { Line(cola, 1) }, null, null, null);

        Assert.False(sale.RegisterPrint());
        Assert.True(sale.RegisterPrint());
        Assert.Equal(2, sale.PrintCount);
    }
}
