using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Tests.Inventory;

public sealed class StockReceiptTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private static Shift OpenShift(decimal opening)
        => new BusinessDay(new DateOnly(2026, 11, 21)).OpenShift(Staff, opening);

    private static Product Prod(string name = "Cola")
        => Product.CreateActive(name, null, null, "Drinks", "can", 20m, Admin);

    private static StockReceipt.Line Line(Product p, decimal qty, decimal cost)
        => new(p, qty, cost, null);

    [Fact]
    public void Receive_RaisesStock_TakesCashOut_AndStaysPending()
    {
        var shift = OpenShift(500m);
        var cola = Prod();

        var receipt = StockReceipt.Receive(shift, Staff, "Supplier", new[] { Line(cola, 10, 12m) });

        Assert.Equal(StockReceiptStatus.Pending, receipt.Status);
        Assert.Equal(120m, receipt.TotalCost);
        Assert.Equal(10m, cola.StockQuantity);
        Assert.Equal(380m, shift.ExpectedCash);
    }

    [Fact]
    public void Receive_FailsWhenDrawerCannotCoverTotal_AndNothingMoves()
    {
        var shift = OpenShift(50m);
        var cola = Prod();

        Assert.Throws<BusinessRuleValidationException>(() =>
            StockReceipt.Receive(shift, Staff, null, new[] { Line(cola, 10, 12m) }));

        Assert.Equal(0m, cola.StockQuantity);
        Assert.Equal(50m, shift.ExpectedCash);
    }

    [Fact]
    public void Receive_RejectsEmptyAndDuplicateLines()
    {
        var shift = OpenShift(500m);
        var cola = Prod();

        Assert.Throws<BusinessRuleValidationException>(() =>
            StockReceipt.Receive(shift, Staff, null, Array.Empty<StockReceipt.Line>()));
        Assert.Throws<BusinessRuleValidationException>(() =>
            StockReceipt.Receive(shift, Staff, null, new[] { Line(cola, 1, 1m), Line(cola, 2, 1m) }));
    }

    [Fact]
    public void Approve_Twice_Fails()
    {
        var receipt = StockReceipt.Receive(OpenShift(500m), Staff, null, new[] { Line(Prod(), 1, 5m) });
        receipt.Approve(Admin);

        Assert.Equal(StockReceiptStatus.Approved, receipt.Status);
        Assert.Throws<BusinessRuleValidationException>(() => receipt.Approve(Admin));
    }

    [Fact]
    public void Reject_ReverseCash_RestoresStockAndCash()
    {
        var shift = OpenShift(500m);
        var cola = Prod();
        var receipt = StockReceipt.Receive(shift, Staff, null, new[] { Line(cola, 10, 12m) });

        receipt.Reject(Admin, "wrong goods", ReceiptRejectionOutcome.ReverseCash,
            new Dictionary<Guid, Product> { [cola.Id] = cola }, shift);

        Assert.Equal(StockReceiptStatus.Rejected, receipt.Status);
        Assert.Equal(0m, cola.StockQuantity);
        Assert.Equal(500m, shift.ExpectedCash);
    }

    [Fact]
    public void Reject_ChargeToStaff_ReversesStockButNotCash()
    {
        var shift = OpenShift(500m);
        var cola = Prod();
        var receipt = StockReceipt.Receive(shift, Staff, null, new[] { Line(cola, 10, 12m) });

        receipt.Reject(Admin, "not authorised", ReceiptRejectionOutcome.ChargeToStaff,
            new Dictionary<Guid, Product> { [cola.Id] = cola }, null);

        Assert.Equal(0m, cola.StockQuantity);
        Assert.Equal(380m, shift.ExpectedCash);
        Assert.Equal(ReceiptRejectionOutcome.ChargeToStaff, receipt.RejectionOutcome);
    }

    [Fact]
    public void Reject_FailsWhenStockAlreadySold_AndLeavesReceiptPending()
    {
        var shift = OpenShift(500m);
        var cola = Prod();
        var receipt = StockReceipt.Receive(shift, Staff, null, new[] { Line(cola, 10, 12m) });
        cola.ApplyMovement(StockMovementType.Waste, 4, Staff, "spilled");

        Assert.Throws<BusinessRuleValidationException>(() =>
            receipt.Reject(Admin, "bad", ReceiptRejectionOutcome.ReverseCash,
                new Dictionary<Guid, Product> { [cola.Id] = cola }, shift));
        Assert.Equal(StockReceiptStatus.Pending, receipt.Status);
    }

    [Fact]
    public void Reject_RequiresReasonAndReturnShift()
    {
        var shift = OpenShift(500m);
        var cola = Prod();
        var products = new Dictionary<Guid, Product> { [cola.Id] = cola };
        var receipt = StockReceipt.Receive(shift, Staff, null, new[] { Line(cola, 1, 5m) });

        Assert.Throws<BusinessRuleValidationException>(() =>
            receipt.Reject(Admin, " ", ReceiptRejectionOutcome.ChargeToStaff, products, null));
        Assert.Throws<BusinessRuleValidationException>(() =>
            receipt.Reject(Admin, "x", ReceiptRejectionOutcome.ReverseCash, products, null));
    }
}
