using System.Text.Json;
using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Common.Result;
using GHOST.Application.Settings;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Application.Sales;

public sealed record SaleLineInput(Guid ProductId, decimal Quantity);

public sealed record DiscountRequest(DiscountType Type, decimal Value, string? Reason);

public sealed record CheckoutCommand(
    Guid ShiftId,
    Guid ActorUserId,
    bool ActorIsAdmin,
    IReadOnlyList<SaleLineInput> Lines,
    Guid? SessionId,
    DiscountRequest? Discount,
    string? IdempotencyKey);

public sealed record SaleItemDto(
    string ItemType, string Description, decimal Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record SaleDto(
    Guid Id,
    string InvoiceNumber,
    Guid ShiftId,
    Guid CreatedById,
    Guid? SessionId,
    string Status,
    decimal Subtotal,
    string DiscountType,
    decimal DiscountAmount,
    decimal Total,
    decimal PaidTotal,
    int PrintCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    IReadOnlyList<SaleItemDto> Items);

public sealed record InvoicePrintDto(SaleDto Sale, bool IsCopy, string Label);

/// <summary>Checkout, refund and print for invoices (SRS FR-50..57).</summary>
public sealed class SaleHandler
{
    private readonly ISaleRepository _sales;
    private readonly IProductRepository _products;
    private readonly IShiftRepository _shifts;
    private readonly ISessionRepository _sessions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _audit;
    private readonly IRejectionAudit? _rejections;
    private readonly ISettingsRepository? _settings;

    public SaleHandler(
        ISaleRepository sales,
        IProductRepository products,
        IShiftRepository shifts,
        ISessionRepository sessions,
        IUnitOfWork unitOfWork,
        IAuditTrail audit,
        IRejectionAudit? rejections = null,
        ISettingsRepository? settings = null)
    {
        _settings = settings;
        _rejections = rejections;
        _sales = sales;
        _products = products;
        _shifts = shifts;
        _sessions = sessions;
        _unitOfWork = unitOfWork;
        _audit = audit;
    }

    /// <summary>The Admin setting if it is stored and valid, otherwise the catalog default.</summary>
    private async Task<decimal> ReadSettingAsync(string key, CancellationToken cancellationToken)
    {
        var definition = SettingsCatalog.Find(key)!;
        if (_settings is null)
            return definition.Default;
        var row = await _settings.GetAsync(key, cancellationToken);
        return row is not null
               && decimal.TryParse(row.Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value)
               && value >= definition.Min && value <= definition.Max
            ? value
            : definition.Default;
    }

    private static bool SameCheckout(Sale existing, CheckoutCommand command)
        => existing.CreatedById == command.ActorUserId
           && existing.ShiftId == command.ShiftId
           // A null fingerprint exists only on invoices created before the fingerprint column (migration SaleRequestFingerprint).
           // A fingerprint stored before the discount reason was part of it is matched by the legacy form.
           && (existing.RequestFingerprint is null
               || existing.RequestFingerprint == Fingerprint(command)
               || existing.RequestFingerprint == LegacyFingerprint(command));

    /// <summary>Canonical hash of the request content (lines, session, discount type, value and reason). Order of lines does not matter.</summary>
    internal static string Fingerprint(CheckoutCommand command) => Hash(command, includeDiscountReason: true);

    /// <summary>The hash as it was before the discount reason was added. Only used to recognise invoices stored earlier.</summary>
    private static string LegacyFingerprint(CheckoutCommand command) => Hash(command, includeDiscountReason: false);

    private static string Hash(CheckoutCommand command, bool includeDiscountReason)
    {
        var lines = string.Join(";", command.Lines
            .OrderBy(l => l.ProductId)
            .Select(l => FormattableString.Invariant($"{l.ProductId:N}x{l.Quantity:0.####}")));
        var discount = command.Discount is { Type: not DiscountType.None } d
            ? FormattableString.Invariant($"{(int)d.Type}:{d.Value:0.####}")
              + (includeDiscountReason ? "|" + (d.Reason ?? string.Empty).Trim() : string.Empty)
            : "none";
        var raw = FormattableString.Invariant($"{command.ShiftId:N}|{command.SessionId:N}|{lines}|{discount}");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
    }

    public async Task<Result<SaleDto>> CheckoutCashAsync(
        CheckoutCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await CheckoutCoreAsync(command, cancellationToken);
        if (!result.IsSuccess)
            await _rejections.RejectedAsync(command.ActorUserId, "Sale.CheckoutRejected", "Shift", command.ShiftId, result.Error, cancellationToken);
        return result;
    }

    private async Task<Result<SaleDto>> CheckoutCoreAsync(
        CheckoutCommand command,
        CancellationToken cancellationToken)
    {
        // Same key again returns the original invoice instead of charging twice (FR-54).
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existing = await _sales.GetByIdempotencyKeyAsync(command.IdempotencyKey.Trim(), cancellationToken);
            if (existing is not null)
            {
                return SameCheckout(existing, command)
                    ? Result<SaleDto>.Success(ToDto(existing))
                    : Result<SaleDto>.Failure("This idempotency key was already used for a different invoice.");
            }
        }

        if (command.Discount is { Type: not DiscountType.None } && !command.ActorIsAdmin)
            return Result<SaleDto>.Failure("Only an Admin can apply a discount.");

        var shift = await _shifts.GetWithCashAsync(command.ShiftId, cancellationToken);
        if (shift is null)
            return Result<SaleDto>.Failure("Shift was not found.");

        var ids = command.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = (await _products.GetManyAsync(ids, cancellationToken)).ToDictionary(p => p.Id);
        if (products.Count != ids.Count)
            return Result<SaleDto>.Failure("A product on the invoice was not found.");

        Sale.GamingLine? gaming = null;
        if (command.SessionId is { } sessionId)
        {
            var session = await _sessions.GetByIdAsync(sessionId, cancellationToken);
            if (session is null)
                return Result<SaleDto>.Failure("Session was not found.");

            if (session.NeedsReview)
                return Result<SaleDto>.Failure("This session is waiting for an Admin review after a power outage.");

            if (await _sales.SessionIsInvoicedAsync(sessionId, cancellationToken))
                return Result<SaleDto>.Failure("This session already has an invoice (a refunded invoice also counts, the session is not invoiced again).");

            gaming = new Sale.GamingLine(session, "Gaming session");
        }

        try
        {
            var invoiceNumber = await _sales.NextInvoiceNumberAsync(DateTimeOffset.UtcNow, cancellationToken);

            var discount = command.Discount is { Type: not DiscountType.None } d
                ? new Sale.DiscountInput(d.Type, d.Value, d.Reason, command.ActorUserId,
                    await ReadSettingAsync(SettingsCatalog.MaxProductDiscountPercent, cancellationToken),
                    await ReadSettingAsync(SettingsCatalog.MaxGamingDiscountPercent, cancellationToken),
                    await ReadSettingAsync(SettingsCatalog.AllowZeroTotalInvoice, cancellationToken) == 1m)
                : null;

            var sale = Sale.CheckoutCash(
                shift,
                command.ActorUserId,
                invoiceNumber,
                command.Lines.Select(l => new Sale.ProductLine(products[l.ProductId], l.Quantity)).ToList(),
                gaming,
                discount,
                command.IdempotencyKey);

            sale.SetRequestFingerprint(Fingerprint(command));
            await _sales.AddAsync(sale, cancellationToken);

            _audit.Record(
                command.ActorUserId,
                discount is null ? "Sale.Paid" : "Sale.PaidWithDiscount",
                "Sale",
                sale.Id.ToString(),
                JsonSerializer.Serialize(new
                {
                    sale.InvoiceNumber,
                    sale.Subtotal,
                    sale.DiscountAmount,
                    sale.Total,
                    sale.DiscountReason
                }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<SaleDto>.Success(ToDto(sale));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<SaleDto>.Failure(exception.Message);
        }
        catch (PersistenceConflictException)
        {
            // Two identical requests at the same instant (double tap, retry after timeout): the other one
            // won the unique index on the idempotency key. Return its invoice instead of an error.
            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var winner = await _sales.GetByIdempotencyKeyAsync(command.IdempotencyKey.Trim(), cancellationToken);
                if (winner is not null && SameCheckout(winner, command))
                    return Result<SaleDto>.Success(ToDto(winner));
            }

            return Result<SaleDto>.Failure(
                "The stock or the drawer changed while you were selling. Please try again.");
        }
    }

    public async Task<Result<SaleDto>> RefundAsync(
        Guid saleId,
        Guid adminUserId,
        string reason,
        bool returnToStock,
        Guid? cashShiftId,
        CancellationToken cancellationToken = default)
    {
        var result = await RefundCoreAsync(saleId, adminUserId, reason, returnToStock, cashShiftId, cancellationToken);
        if (!result.IsSuccess)
            await _rejections.RejectedAsync(adminUserId, "Sale.RefundRejected", "Sale", saleId, result.Error, cancellationToken);
        return result;
    }

    private async Task<Result<SaleDto>> RefundCoreAsync(
        Guid saleId,
        Guid adminUserId,
        string reason,
        bool returnToStock,
        Guid? requestedShiftId,
        CancellationToken cancellationToken)
    {
        var sale = await _sales.GetAsync(saleId, cancellationToken);
        if (sale is null)
            return Result<SaleDto>.Failure("Invoice was not found.");

        // The server decides where the cash comes from: the acting Admin's own shift that is open right now.
        // A shift id sent by the client is only a check; it can never choose a different shift.
        var openShifts = await _shifts.GetOpenShiftsForUserWithCashAsync(adminUserId, cancellationToken);
        if (openShifts.Count == 0)
            return Result<SaleDto>.Failure("A refund needs your own open shift. Open a shift first.");
        if (openShifts.Count > 1)
        {
            // Unexpected state: never guess which shift the cash should leave from.
            await _rejections.RejectedAsync(adminUserId, "Sale.RefundRejected", "Sale", saleId, "More than one open shift for this Admin.", cancellationToken);
            return Result<SaleDto>.Failure("You have more than one open shift. Close the extra shift before refunding.");
        }
        var shift = openShifts[0];
        if (requestedShiftId is { } requested && requested != shift.Id)
            return Result<SaleDto>.Failure("That shift is not allowed for this refund. Refunds post to your own open shift.");

        var ids = sale.Items.Where(i => i.ProductId is not null).Select(i => i.ProductId!.Value).Distinct().ToList();
        var products = (await _products.GetManyAsync(ids, cancellationToken)).ToDictionary(p => p.Id);

        try
        {
            sale.RefundAll(adminUserId, reason, returnToStock, shift, products);

            _audit.Record(
                adminUserId,
                "Sale.Refunded",
                "Sale",
                sale.Id.ToString(),
                JsonSerializer.Serialize(new { sale.InvoiceNumber, amount = sale.PaidTotal, returnToStock, reason }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<SaleDto>.Success(ToDto(sale));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<SaleDto>.Failure(exception.Message);
        }
        catch (PersistenceConflictException)
        {
            return Result<SaleDto>.Failure("The stock or the drawer changed. Please try again.");
        }
    }

    /// <summary>Counts a print and says whether it must be labelled as a copy (FR-56).</summary>
    public async Task<Result<InvoicePrintDto>> PrintAsync(
        Guid saleId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var sale = await _sales.GetAsync(saleId, cancellationToken);
        if (sale is null)
            return Result<InvoicePrintDto>.Failure("Invoice was not found.");

        try
        {
            var isCopy = sale.RegisterPrint();
            _audit.Record(
                actorUserId,
                isCopy ? "Sale.Reprinted" : "Sale.Printed",
                "Sale",
                sale.Id.ToString(),
                JsonSerializer.Serialize(new { sale.InvoiceNumber, sale.PrintCount }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<InvoicePrintDto>.Success(new InvoicePrintDto(ToDto(sale), isCopy, isCopy ? "نسخة" : "أصل"));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<InvoicePrintDto>.Failure(exception.Message);
        }
        catch (PersistenceConflictException)
        {
            return Result<InvoicePrintDto>.Failure("Please try printing again.");
        }
    }

    public async Task<Result<SaleDto>> GetAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        var sale = await _sales.GetAsync(saleId, cancellationToken);
        return sale is null
            ? Result<SaleDto>.Failure("Invoice was not found.")
            : Result<SaleDto>.Success(ToDto(sale));
    }

    public async Task<IReadOnlyList<SaleDto>> ListAsync(
        Guid? shiftId,
        SaleStatus? status,
        Guid? createdById,
        CancellationToken cancellationToken = default)
        => (await _sales.ListAsync(shiftId, status, createdById, cancellationToken)).Select(ToDto).ToList();

    public static SaleDto ToDto(Sale s) => new(
        s.Id, s.InvoiceNumber, s.ShiftId, s.CreatedById, s.SessionId, s.Status.ToString(), s.Subtotal,
        s.DiscountType.ToString(), s.DiscountAmount, s.Total, s.PaidTotal, s.PrintCount, s.CreatedAt, s.PaidAt,
        s.Items.Select(i => new SaleItemDto(i.ItemType.ToString(), i.DescriptionSnapshot, i.Quantity, i.UnitPrice, i.LineTotal)).ToList());
}
