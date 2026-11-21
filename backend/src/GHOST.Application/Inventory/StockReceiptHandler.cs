using System.Text.Json;
using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Common.Result;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Application.Inventory;

public sealed record ReceiptLineInput(Guid ProductId, decimal Quantity, decimal UnitCost, DateOnly? ExpiryDate);

public sealed record CreateReceiptCommand(
    Guid ShiftId,
    Guid ActorUserId,
    string? SupplierName,
    IReadOnlyList<ReceiptLineInput> Lines);

public sealed record RejectReceiptCommand(
    Guid ReceiptId,
    Guid AdminUserId,
    string Reason,
    ReceiptRejectionOutcome Outcome,
    Guid? CashReturnShiftId);

public sealed record ReceiptItemDto(Guid ProductId, decimal Quantity, decimal UnitCost, decimal LineTotal, DateOnly? ExpiryDate);

public sealed record ReceiptDto(
    Guid Id,
    Guid ShiftId,
    Guid CreatedById,
    string? SupplierName,
    string Status,
    decimal TotalCost,
    string? RejectionReason,
    string? RejectionOutcome,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ReceiptItemDto> Items);

/// <summary>Stock receipt workflow (SRS FR-34, FR-35).</summary>
public sealed class StockReceiptHandler
{
    private readonly IStockReceiptRepository _receipts;
    private readonly IProductRepository _products;
    private readonly IShiftRepository _shifts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _audit;

    public StockReceiptHandler(
        IStockReceiptRepository receipts,
        IProductRepository products,
        IShiftRepository shifts,
        IUnitOfWork unitOfWork,
        IAuditTrail audit)
    {
        _receipts = receipts;
        _products = products;
        _shifts = shifts;
        _unitOfWork = unitOfWork;
        _audit = audit;
    }

    public async Task<Result<ReceiptDto>> CreateAsync(
        CreateReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        var shift = await _shifts.GetWithCashAsync(command.ShiftId, cancellationToken);
        if (shift is null)
            return Result<ReceiptDto>.Failure("Shift was not found.");

        var ids = command.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = (await _products.GetManyAsync(ids, cancellationToken)).ToDictionary(p => p.Id);
        if (products.Count != ids.Count)
            return Result<ReceiptDto>.Failure("A product on the receipt was not found.");

        try
        {
            var lines = command.Lines
                .Select(l => new StockReceipt.Line(products[l.ProductId], l.Quantity, l.UnitCost, l.ExpiryDate))
                .ToList();

            var receipt = StockReceipt.Receive(shift, command.ActorUserId, command.SupplierName, lines);
            await _receipts.AddAsync(receipt, cancellationToken);

            _audit.Record(
                command.ActorUserId,
                "StockReceipt.Created",
                "StockReceipt",
                receipt.Id.ToString(),
                JsonSerializer.Serialize(new { receipt.TotalCost, items = receipt.Items.Count }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<ReceiptDto>.Success(ToDto(receipt));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<ReceiptDto>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<ReceiptDto>.Failure(exception.Message);
        }
    }

    public async Task<Result<ReceiptDto>> ApproveAsync(
        Guid receiptId,
        Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        var receipt = await _receipts.GetAsync(receiptId, cancellationToken);
        if (receipt is null)
            return Result<ReceiptDto>.Failure("Receipt was not found.");

        try
        {
            receipt.Approve(adminUserId);
            _audit.Record(adminUserId, "StockReceipt.Approved", "StockReceipt", receipt.Id.ToString(), null);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<ReceiptDto>.Success(ToDto(receipt));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<ReceiptDto>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<ReceiptDto>.Failure(exception.Message);
        }
    }

    public async Task<Result<ReceiptDto>> RejectAsync(
        RejectReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        var receipt = await _receipts.GetAsync(command.ReceiptId, cancellationToken);
        if (receipt is null)
            return Result<ReceiptDto>.Failure("Receipt was not found.");

        Shift? returnShift = null;
        if (command.Outcome == ReceiptRejectionOutcome.ReverseCash)
        {
            if (command.CashReturnShiftId is null)
                return Result<ReceiptDto>.Failure("Choose the open shift that receives the returned cash.");

            returnShift = await _shifts.GetWithCashAsync(command.CashReturnShiftId.Value, cancellationToken);
            if (returnShift is null)
                return Result<ReceiptDto>.Failure("Shift was not found.");
        }

        var ids = receipt.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = (await _products.GetManyAsync(ids, cancellationToken)).ToDictionary(p => p.Id);

        try
        {
            receipt.Reject(command.AdminUserId, command.Reason, command.Outcome, products, returnShift);

            _audit.Record(
                command.AdminUserId,
                "StockReceipt.Rejected",
                "StockReceipt",
                receipt.Id.ToString(),
                JsonSerializer.Serialize(new
                {
                    outcome = command.Outcome.ToString(),
                    receipt.TotalCost,
                    receipt.RejectionReason,
                    chargedToStaffId = command.Outcome == ReceiptRejectionOutcome.ChargeToStaff
                        ? receipt.CreatedById
                        : (Guid?)null
                }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<ReceiptDto>.Success(ToDto(receipt));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<ReceiptDto>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<ReceiptDto>.Failure(exception.Message);
        }
    }

    public async Task<IReadOnlyList<ReceiptDto>> ListAsync(
        StockReceiptStatus? status,
        Guid? createdById,
        CancellationToken cancellationToken = default)
        => (await _receipts.ListAsync(status, createdById, cancellationToken)).Select(ToDto).ToList();

    private static ReceiptDto ToDto(StockReceipt r) => new(
        r.Id, r.ShiftId, r.CreatedById, r.SupplierName, r.Status.ToString(), r.TotalCost,
        r.RejectionReason, r.RejectionOutcome?.ToString(), r.CreatedAt,
        r.Items.Select(i => new ReceiptItemDto(i.ProductId, i.Quantity, i.UnitCost, i.LineTotal, i.ExpiryDate)).ToList());
}
