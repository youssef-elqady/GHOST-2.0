using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;
using GHOST.Domain.Services;

namespace GHOST.Tests.Inventory;

public sealed class StocktakeTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private static Product Stocked(string name, decimal qty)
    {
        var p = Product.CreateActive(name, null, null, "c", "u", 10m, Admin);
        p.ApplyMovement(StockMovementType.Purchase, qty, Admin, "seed", unitCost: 5m);
        return p;
    }

    [Fact]
    public void Start_SnapshotsSystemQuantities_OfTrackedProductsOnly()
    {
        var a = Stocked("A", 10);
        var untracked = Product.CreateActive("Svc", null, null, "c", "u", 10m, Admin, trackStock: false);

        var st = Stocktake.Start(Staff, new[] { a, untracked });

        Assert.Single(st.Items);
        Assert.Equal(10m, st.Items.Single().SystemQuantity);
    }

    [Fact]
    public void Submit_RequiresEveryProductCounted()
    {
        var st = Stocktake.Start(Staff, new[] { Stocked("A", 10), Stocked("B", 4) });
        var a = st.Items.First();
        st.RecordCounts(new Dictionary<Guid, decimal> { [a.ProductId] = 9 });

        Assert.Throws<BusinessRuleValidationException>(() => st.Submit());
    }

    [Fact]
    public void Approve_AppliesShortageAndSurplusMovements()
    {
        var a = Stocked("A", 10);
        var b = Stocked("B", 4);
        var st = Stocktake.Start(Staff, new[] { a, b });
        st.RecordCounts(new Dictionary<Guid, decimal> { [a.Id] = 7, [b.Id] = 6 });
        st.Submit();

        st.Approve(Admin, "ok", new Dictionary<Guid, Product> { [a.Id] = a, [b.Id] = b });

        Assert.Equal(StocktakeStatus.Approved, st.Status);
        Assert.Equal(7m, a.StockQuantity);
        Assert.Equal(6m, b.StockQuantity);
        Assert.Equal(-3m, a.Movements.Last().QtyDelta);
        Assert.Equal(StockMovementType.StocktakeAdjustment, b.Movements.Last().Type);
    }

    [Fact]
    public void Approve_UsesDifferenceFromSnapshot_SoSalesDuringCountSurvive()
    {
        var a = Stocked("A", 10);
        var st = Stocktake.Start(Staff, new[] { a });
        // Two units sold after the count started; the shelf still held 10 when counted.
        a.ApplyMovement(StockMovementType.Sale, 2, Staff, "sale");
        st.RecordCounts(new Dictionary<Guid, decimal> { [a.Id] = 10 });
        st.Submit();

        st.Approve(Admin, null, new Dictionary<Guid, Product> { [a.Id] = a });

        Assert.Equal(8m, a.StockQuantity);
    }

    [Fact]
    public void Counts_CannotChangeAfterSubmit_AndDecisionIsOnce()
    {
        var a = Stocked("A", 10);
        var st = Stocktake.Start(Staff, new[] { a });
        st.RecordCounts(new Dictionary<Guid, decimal> { [a.Id] = 10 });
        st.Submit();

        Assert.Throws<BusinessRuleValidationException>(() =>
            st.RecordCounts(new Dictionary<Guid, decimal> { [a.Id] = 5 }));

        st.Reject(Admin, "recount");
        Assert.Throws<BusinessRuleValidationException>(() =>
            st.Approve(Admin, null, new Dictionary<Guid, Product> { [a.Id] = a }));
    }

    [Fact]
    public void Counts_RejectNegativeAndUnknownProducts()
    {
        var a = Stocked("A", 10);
        var st = Stocktake.Start(Staff, new[] { a });

        Assert.Throws<BusinessRuleValidationException>(() =>
            st.RecordCounts(new Dictionary<Guid, decimal> { [a.Id] = -1 }));
        Assert.Throws<BusinessRuleValidationException>(() =>
            st.RecordCounts(new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 1 }));
    }

    [Fact]
    public void Ean13_BuildsValidCodes()
    {
        Assert.Equal("2000000000015", Ean13.FromSequence(1));
        Assert.True(Ean13.IsValid(Ean13.FromSequence(123456)));
        Assert.False(Ean13.IsValid("2000000000018"));
        Assert.Equal(13, Ean13.FromSequence(999_999_999).Length);
    }

    [Fact]
    public void AssignBarcode_OnlyOnce()
    {
        var p = Product.CreateActive("A", null, null, "c", "u", 10m, Admin);
        p.AssignBarcode(Ean13.FromSequence(1));
        Assert.Throws<BusinessRuleValidationException>(() => p.AssignBarcode(Ean13.FromSequence(2)));
    }
}
