using System.Text.Json;
using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Common.Result;
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

    public SaleHandler(
        ISaleRepository sales,
        IProductRepository products,
        IShiftRepository shifts,
        ISessionRepository sessions,
        IUnitOfWork unitOfWork,
        IAuditTrail audit)
    {
        _sales = sales;
        _products = products;
        _shifts = shifts;
        _sessions = sessions;
        _unitOfWork = unitOfWork;
        _audit = audit;
    }

    public async Task<Result<SaleDto>> CheckoutCashAsync(
        CheckoutCommand command,
        CancellationToken cancellationToken = default)
    {
        // Same key again returns the original invoice instead of charging twice (FR-54).
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existing = await _sales.GetByIdempotencyKeyAsync(command.IdempotencyKey.Trim(), cancellationToken);
            if (existing is not null)
            {
                return existing.CreatedById == command.ActorUserId && existing.ShiftId == command.ShiftId
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
                return Result<SaleDto>.Failure("This session already has an invoice.");

            gaming = new Sale.GamingLine(session, "Gaming session");
        }

        try
        {
            var invoiceNumber = await _sales.NextInvoiceNumberAsync(DateTimeOffset.UtcNow, cancellationToken);

            var discount = command.Discount is { Type: not DiscountType.None } d
                ? new Sale.DiscountInput(d.Type, d.Value, d.Reason, command.ActorUserId)
                : null;

            var sale = Sale.CheckoutCash(
                shift,
                command.ActorUserId,
                invoiceNumber,
                command.Lines.Select(l => new Sale.ProductLine(products[l.ProductId], l.Quantity)).ToList(),
                gaming,
                discount,
                command.IdempotencyKey);

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
                if (winner is not null && winner.CreatedById == command.ActorUserId && winner.ShiftId == command.ShiftId)
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
        Guid cashShiftId,
        CancellationToken cancellationToken = default)
    {
        var sale = await _sales.GetAsync(saleId, cancellationToken);
        if (sale is null)
            return Result<SaleDto>.Failure("Invoice was not found.");

        var shift = await _shifts.GetWithCashAsync(cashShiftId, cancellationToken);
        if (shift is null)
            return Result<SaleDto>.Failure("Shift was not found.");

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
