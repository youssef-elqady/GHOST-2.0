using GHOST.Domain.Common;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

public sealed class StockReceiptItem
{
    public Guid Id { get; private set; }
    public Guid ReceiptId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }

    private StockReceiptItem()
    {
    }

    internal StockReceiptItem(Guid receiptId, Guid productId, decimal quantity, decimal unitCost, DateOnly? expiry)
    {
        Id = Guid.NewGuid();
        ReceiptId = receiptId;
        ProductId = productId;
        Quantity = quantity;
        UnitCost = unitCost;
        ExpiryDate = expiry;
    }

    public decimal LineTotal => decimal.Round(Quantity * UnitCost, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Goods received by Staff (SRS FR-34/35). Stock goes up immediately and cash leaves the drawer
/// in the same step; the receipt stays Pending until an Admin approves or rejects it.
/// </summary>
public sealed class StockReceipt : BaseEntity
{
    private readonly List<StockReceiptItem> _items = new();

    public Guid ShiftId { get; private set; }
    public Guid CreatedById { get; private set; }
    public string? SupplierName { get; private set; }

    public const int MaxReferenceLength = 100;
    public const int MaxNotesLength = 500;

    /// <summary>Purchase invoice number or other reference, optional.</summary>
    public string? InvoiceReference { get; private set; }

    public string? Notes { get; private set; }
    public StockReceiptStatus Status { get; private set; }
    public decimal TotalCost { get; private set; }
    public Guid? DecidedById { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public ReceiptRejectionOutcome? RejectionOutcome { get; private set; }

    public IReadOnlyCollection<StockReceiptItem> Items => _items.AsReadOnly();

    private StockReceipt()
    {
    }

    public sealed record Line(Product Product, decimal Quantity, decimal UnitCost, DateOnly? ExpiryDate);

    /// <summary>
    /// Records the receipt, raises stock for every line and takes the total out of the shift's drawer.
    /// Pure domain logic; the caller persists the shift, products and receipt in one unit of work.
    /// </summary>
    public static StockReceipt Receive(Shift shift, Guid staffId, string? supplierName, IReadOnlyList<Line> lines,
        string? invoiceReference = null, string? notes = null)
    {
        var reference = string.IsNullOrWhiteSpace(invoiceReference) ? null : invoiceReference.Trim();
        var note = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (reference is { Length: > MaxReferenceLength })
            throw new BusinessRuleValidationException($"Invoice reference cannot exceed {MaxReferenceLength} characters.");
        if (note is { Length: > MaxNotesLength })
            throw new BusinessRuleValidationException($"Notes cannot exceed {MaxNotesLength} characters.");

        if (lines.Count == 0)
            throw new BusinessRuleValidationException("A receipt needs at least one item.");

        if (lines.Select(l => l.Product.Id).Distinct().Count() != lines.Count)
            throw new BusinessRuleValidationException("Each product can appear only once per receipt.");

        var receipt = new StockReceipt
        {
            ShiftId = shift.Id,
            CreatedById = staffId,
            SupplierName = string.IsNullOrWhiteSpace(supplierName) ? null : supplierName.Trim(),
            InvoiceReference = reference,
            Notes = note,
            Status = StockReceiptStatus.Pending
        };

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new BusinessRuleValidationException("Quantity must be greater than zero.");
            if (line.UnitCost < 0)
                throw new BusinessRuleValidationException("Unit cost cannot be negative.");

            receipt._items.Add(new StockReceiptItem(
                receipt.Id, line.Product.Id, line.Quantity, line.UnitCost, line.ExpiryDate));
        }

        receipt.TotalCost = receipt._items.Sum(i => i.LineTotal);

        if (receipt.TotalCost > 0)
        {
            // Throws when the drawer cannot cover it or the shift is closed (BD-14).
            shift.RecordCash(
                CashTransactionType.StockPurchase,
                CashDirection.Out,
                receipt.TotalCost,
                staffId,
                reason: "Stock receipt",
                category: "StockPurchase",
                sourceType: nameof(StockReceipt),
                sourceId: receipt.Id,
                idempotencyKey: $"receipt:{receipt.Id}");
        }

        foreach (var line in lines)
        {
            line.Product.ApplyMovement(
                StockMovementType.Purchase,
                line.Quantity,
                staffId,
                "Stock receipt",
                shiftId: shift.Id,
                unitCost: line.UnitCost,
                sourceType: nameof(StockReceipt),
                sourceId: receipt.Id);
        }

        return receipt;
    }

    public void Approve(Guid adminId)
    {
        EnsurePending();
        Status = StockReceiptStatus.Approved;
        DecidedById = adminId;
        DecidedAt = DateTimeOffset.UtcNow;
        MarkAsUpdated();
    }

    /// <summary>
    /// Rejects the receipt: stock is reversed per line and, for <see cref="ReceiptRejectionOutcome.ReverseCash"/>,
    /// the money returns to the given open shift. Fails as a whole if any product has already been sold.
    /// </summary>
    public void Reject(
        Guid adminId,
        string reason,
        ReceiptRejectionOutcome outcome,
        IReadOnlyDictionary<Guid, Product> products,
        Shift? cashReturnShift)
    {
        EnsurePending();

        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleValidationException("A rejection reason is required.");

        if (!Enum.IsDefined(outcome))
            throw new BusinessRuleValidationException("Rejection outcome is invalid.");

        foreach (var item in _items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
                throw new BusinessRuleValidationException("A receipt product was not found.");

            product.ApplyMovement(
                StockMovementType.PurchaseReversal,
                item.Quantity,
                adminId,
                $"Receipt rejected: {reason.Trim()}",
                shiftId: cashReturnShift?.Id,
                unitCost: item.UnitCost,
                sourceType: nameof(StockReceipt),
                sourceId: Id);
        }

        if (outcome == ReceiptRejectionOutcome.ReverseCash && TotalCost > 0)
        {
            if (cashReturnShift is null)
                throw new BusinessRuleValidationException(
                    "Reversing the cash needs an open shift to receive it.");

            cashReturnShift.RecordCash(
                CashTransactionType.StockPurchaseReversal,
                CashDirection.In,
                TotalCost,
                adminId,
                reason: $"Receipt rejected: {reason.Trim()}",
                category: "StockPurchaseReversal",
                sourceType: nameof(StockReceipt),
                sourceId: Id,
                idempotencyKey: $"receipt-reversal:{Id}");
        }

        Status = StockReceiptStatus.Rejected;
        RejectionReason = reason.Trim();
        RejectionOutcome = outcome;
        DecidedById = adminId;
        DecidedAt = DateTimeOffset.UtcNow;
        MarkAsUpdated();
    }

    private void EnsurePending()
    {
        if (Status != StockReceiptStatus.Pending)
            throw new BusinessRuleValidationException("This receipt has already been decided.");
    }
}
