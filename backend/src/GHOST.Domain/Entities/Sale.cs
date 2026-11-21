using GHOST.Domain.Common;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

public sealed class SaleItem
{
    public Guid Id { get; private set; }
    public Guid SaleId { get; private set; }
    public SaleItemType ItemType { get; private set; }
    public Guid? ProductId { get; private set; }
    public Guid? SessionId { get; private set; }
    public string DescriptionSnapshot { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }

    /// <summary>Average cost frozen at the time of sale (FR-53).</summary>
    public decimal UnitCostSnapshot { get; private set; }
    public decimal LineTotal { get; private set; }

    private SaleItem()
    {
    }

    internal static SaleItem ForProduct(Guid saleId, Product product, decimal quantity)
        => new()
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            ItemType = SaleItemType.Product,
            ProductId = product.Id,
            DescriptionSnapshot = product.Name,
            Quantity = quantity,
            UnitPrice = product.SalePrice,
            UnitCostSnapshot = product.AvgCost,
            LineTotal = decimal.Round(quantity * product.SalePrice, 2, MidpointRounding.AwayFromZero)
        };

    internal static SaleItem ForGaming(Guid saleId, Guid sessionId, string description, decimal amount)
        => new()
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            ItemType = SaleItemType.Gaming,
            SessionId = sessionId,
            DescriptionSnapshot = description,
            Quantity = 1,
            UnitPrice = amount,
            UnitCostSnapshot = 0m,
            LineTotal = amount
        };
}

public sealed class Payment
{
    public Guid Id { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid ShiftId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal Amount { get; private set; }
    public Guid ReceivedById { get; private set; }
    public DateTimeOffset PaidAt { get; private set; }

    private Payment()
    {
    }

    internal Payment(Guid saleId, Guid shiftId, PaymentMethod method, decimal amount, Guid receivedById)
    {
        Id = Guid.NewGuid();
        SaleId = saleId;
        ShiftId = shiftId;
        Method = method;
        Amount = amount;
        ReceivedById = receivedById;
        PaidAt = DateTimeOffset.UtcNow;
    }
}

public sealed class Refund
{
    public Guid Id { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid ShiftId { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid ApprovedById { get; private set; }
    public bool ReturnedToStock { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Refund()
    {
    }

    internal Refund(Guid saleId, Guid shiftId, decimal amount, string reason, Guid approvedById, bool returnedToStock)
    {
        Id = Guid.NewGuid();
        SaleId = saleId;
        ShiftId = shiftId;
        Amount = amount;
        Reason = reason;
        ApprovedById = approvedById;
        ReturnedToStock = returnedToStock;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// One invoice combining a gaming session and/or products (SRS 4.5). Created and paid in cash in a
/// single step (FR-54, FR-57): stock, drawer cash and the invoice change together or not at all.
/// Paid invoices are never deleted; a full refund is the only way back (FR-55).
/// </summary>
public sealed class Sale : BaseEntity
{
    public const int MaxReasonLength = 500;

    private readonly List<SaleItem> _items = new();
    private readonly List<Payment> _payments = new();
    private readonly List<Refund> _refunds = new();

    public string InvoiceNumber { get; private set; } = string.Empty;
    public Guid ShiftId { get; private set; }
    public Guid CreatedById { get; private set; }
    public Guid? SessionId { get; private set; }
    public SaleStatus Status { get; private set; }
    public decimal Subtotal { get; private set; }
    public DiscountType DiscountType { get; private set; }
    public decimal DiscountValue { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public string? DiscountReason { get; private set; }
    public Guid? DiscountApprovedById { get; private set; }
    public decimal Total { get; private set; }
    public decimal PaidTotal { get; private set; }
    public int PrintCount { get; private set; }
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this invoice replaces a refunded one for the same session (Rebill): the old invoice, kept untouched.</summary>
    public Guid? ReplacesSaleId { get; private set; }
    public string? RebillReason { get; private set; }

    public void MarkAsRebill(Guid replacesSaleId, string reason)
    {
        if (replacesSaleId == Guid.Empty)
            throw new BusinessRuleValidationException("The replaced invoice is required.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
            throw new BusinessRuleValidationException("A reason is required to re-bill a refunded session.");
        ReplacesSaleId = replacesSaleId;
        RebillReason = reason.Trim().Length > 300 ? reason.Trim()[..300] : reason.Trim();
    }

    /// <summary>
    /// SHA-256 of what this checkout asked for (lines, session, discount). A retry with the same key must match it,
    /// so a key can never be reused for a different purchase. Two separate purchases use two different keys.
    /// </summary>
    public string? RequestFingerprint { get; private set; }

    public void SetRequestFingerprint(string fingerprint) => RequestFingerprint = fingerprint;
    public DateTimeOffset? PaidAt { get; private set; }

    public IReadOnlyCollection<SaleItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<Payment> Payments => _payments.AsReadOnly();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    private Sale()
    {
    }

    public sealed record ProductLine(Product Product, decimal Quantity);

    public sealed record DiscountInput(DiscountType Type, decimal Value, string? Reason, Guid ApprovedById);

    /// <summary>Total a completed gaming session adds to an invoice.</summary>
    public sealed record GamingLine(Session Session, string Description);

    public static Sale CheckoutCash(
        Shift shift,
        Guid staffId,
        string invoiceNumber,
        IReadOnlyList<ProductLine> products,
        GamingLine? gaming,
        DiscountInput? discount,
        string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new BusinessRuleValidationException("Invoice number is required.");

        if (products.Count == 0 && gaming is null)
            throw new BusinessRuleValidationException("An invoice needs at least one item.");

        if (products.Select(p => p.Product.Id).Distinct().Count() != products.Count)
            throw new BusinessRuleValidationException("Each product can appear only once per invoice.");

        var sale = new Sale
        {
            InvoiceNumber = invoiceNumber,
            ShiftId = shift.Id,
            CreatedById = staffId,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            Status = SaleStatus.Draft
        };

        if (gaming is not null)
        {
            var session = gaming.Session;
            if (session.Status != SessionStatus.Completed)
                throw new BusinessRuleValidationException("Only a completed session can be invoiced.");

            var amount = session.GetTotalAmount();
            sale.SessionId = session.Id;
            sale._items.Add(SaleItem.ForGaming(sale.Id, session.Id, gaming.Description, amount));
        }

        foreach (var line in products)
        {
            if (line.Quantity <= 0 || decimal.Round(line.Quantity, 3) != line.Quantity)
                throw new BusinessRuleValidationException("Quantity must be greater than zero.");

            if (!line.Product.IsSellable)
                throw new BusinessRuleValidationException(
                    $"'{line.Product.Name}' cannot be sold (waiting for an Admin price, or disabled).");

            // Freeze price and cost before the stock movement can change the average cost.
            sale._items.Add(SaleItem.ForProduct(sale.Id, line.Product, line.Quantity));
        }

        sale.Subtotal = sale._items.Sum(i => i.LineTotal);
        sale.ApplyDiscount(discount);
        sale.Total = sale.Subtotal - sale.DiscountAmount;

        // Every invoice total is a whole number of EGP (BD-13).
        if (decimal.Floor(sale.Total) != sale.Total)
            throw new BusinessRuleValidationException(
                "Invoice total must be a whole number of EGP. Use whole quantities for priced items.");

        foreach (var line in products)
        {
            if (line.Product.TrackStock)
            {
                line.Product.ApplyMovement(
                    StockMovementType.Sale,
                    line.Quantity,
                    staffId,
                    $"Sale {invoiceNumber}",
                    shiftId: shift.Id,
                    sourceType: nameof(Sale),
                    sourceId: sale.Id);
            }
        }

        if (sale.Total > 0)
        {
            shift.RecordCash(
                CashTransactionType.SaleCash,
                CashDirection.In,
                sale.Total,
                staffId,
                reason: $"Sale {invoiceNumber}",
                category: "Sale",
                sourceType: nameof(Sale),
                sourceId: sale.Id,
                idempotencyKey: $"sale:{sale.Id}");

            sale._payments.Add(new Payment(sale.Id, shift.Id, PaymentMethod.Cash, sale.Total, staffId));
        }

        sale.PaidTotal = sale.Total;
        sale.Status = SaleStatus.Paid;
        sale.PaidAt = DateTimeOffset.UtcNow;
        return sale;
    }

    /// <summary>
    /// Full refund by an Admin (FR-55). Cash goes out of the given open shift. Items that are
    /// returned to stock add inventory back; others (for example spoiled goods) do not.
    /// </summary>
    public void RefundAll(
        Guid adminId,
        string reason,
        bool returnToStock,
        Shift cashShift,
        IReadOnlyDictionary<Guid, Product> products)
    {
        if (Status != SaleStatus.Paid)
            throw new BusinessRuleValidationException("Only a paid invoice can be refunded.");

        // Cash is returned from the shift open NOW, not the invoice's own shift. Check before changing anything.
        if (cashShift.Status != ShiftStatus.Open)
            throw new BusinessRuleValidationException("A refund needs an open shift. Open a shift first.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleValidationException("A refund reason is required.");

        var trimmed = reason.Trim();
        if (trimmed.Length > MaxReasonLength)
            throw new BusinessRuleValidationException($"Reason cannot exceed {MaxReasonLength} characters.");

        if (returnToStock)
        {
            foreach (var item in _items.Where(i => i.ItemType == SaleItemType.Product))
            {
                if (!products.TryGetValue(item.ProductId!.Value, out var product))
                    throw new BusinessRuleValidationException("A product on the invoice was not found.");

                if (!product.TrackStock)
                    continue;

                product.ApplyMovement(
                    StockMovementType.SaleReturn,
                    item.Quantity,
                    adminId,
                    $"Refund of {InvoiceNumber}: {trimmed}",
                    shiftId: cashShift.Id,
                    unitCost: item.UnitCostSnapshot,
                    sourceType: nameof(Sale),
                    sourceId: Id);
            }
        }

        if (PaidTotal > 0)
        {
            cashShift.RecordCash(
                CashTransactionType.Refund,
                CashDirection.Out,
                PaidTotal,
                adminId,
                reason: $"Refund of {InvoiceNumber}: {trimmed}",
                category: "Refund",
                sourceType: nameof(Sale),
                sourceId: Id,
                idempotencyKey: $"refund:{Id}");
        }

        _refunds.Add(new Refund(Id, cashShift.Id, PaidTotal, trimmed, adminId, returnToStock));
        Status = SaleStatus.Refunded;
        MarkAsUpdated();
    }

    /// <summary>Counts a print; any print after the first is a copy (FR-56).</summary>
    public bool RegisterPrint()
    {
        if (Status is not (SaleStatus.Paid or SaleStatus.Refunded))
            throw new BusinessRuleValidationException("Only a paid invoice can be printed.");

        var isCopy = PrintCount > 0;
        PrintCount++;
        MarkAsUpdated();
        return isCopy;
    }

    private void ApplyDiscount(DiscountInput? discount)
    {
        if (discount is null || discount.Type == DiscountType.None)
            return;

        if (discount.ApprovedById == Guid.Empty)
            throw new BusinessRuleValidationException("A discount needs an Admin approval.");

        if (string.IsNullOrWhiteSpace(discount.Reason))
            throw new BusinessRuleValidationException("A discount reason is required.");

        if (discount.Value <= 0)
            throw new BusinessRuleValidationException("Discount must be greater than zero.");

        decimal amount;
        switch (discount.Type)
        {
            case DiscountType.Fixed:
                amount = discount.Value;
                break;
            case DiscountType.Percent:
                if (discount.Value > 100)
                    throw new BusinessRuleValidationException("Percent discount cannot exceed 100.");
                // Round the discount down so the invoice total stays a whole number.
                amount = decimal.Floor(Subtotal * discount.Value / 100m);
                break;
            default:
                throw new BusinessRuleValidationException("Discount type is invalid.");
        }

        if (amount > Subtotal)
            throw new BusinessRuleValidationException("Discount cannot exceed the subtotal.");

        DiscountType = discount.Type;
        DiscountValue = discount.Value;
        DiscountAmount = amount;
        DiscountReason = discount.Reason.Trim();
        DiscountApprovedById = discount.ApprovedById;
    }
}
