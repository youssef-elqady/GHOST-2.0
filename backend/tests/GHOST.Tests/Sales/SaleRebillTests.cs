using GHOST.Domain.Exceptions;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;

namespace GHOST.Tests.Sales;

public sealed class SaleRebillTests
{
    private static Sale NewSale()
    {
        var staff = Guid.NewGuid();
        var shift = new BusinessDay(new DateOnly(2026, 11, 21)).OpenShift(staff, 100m);
        var cola = Product.CreateActive("Cola", null, null, "Drinks", "can", 20m, staff);
        cola.ApplyMovement(StockMovementType.Purchase, 10, staff, "seed", unitCost: 12m);
        return Sale.CheckoutCash(shift, staff, "GS-2026-00001",
            new[] { new Sale.ProductLine(cola, 1) }, null, null, null);
    }

    [Fact]
    public void Rebill_link_keeps_old_invoice_id_and_reason()
    {
        var sale = NewSale();
        var old = Guid.NewGuid();
        sale.MarkAsRebill(old, "  customer paid wrong amount  ");
        Assert.Equal(old, sale.ReplacesSaleId);
        Assert.Equal("customer paid wrong amount", sale.RebillReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    public void Rebill_without_a_real_reason_is_rejected(string? reason)
    {
        var sale = NewSale();
        Assert.Throws<BusinessRuleValidationException>(() => sale.MarkAsRebill(Guid.NewGuid(), reason!));
        Assert.Null(sale.ReplacesSaleId);
    }

    [Fact]
    public void Rebill_needs_the_replaced_invoice()
        => Assert.Throws<BusinessRuleValidationException>(() => NewSale().MarkAsRebill(Guid.Empty, "reason ok"));

    [Fact]
    public void Long_reason_is_clipped_to_300()
    {
        var sale = NewSale();
        sale.MarkAsRebill(Guid.NewGuid(), new string('x', 500));
        Assert.Equal(300, sale.RebillReason!.Length);
    }
}
