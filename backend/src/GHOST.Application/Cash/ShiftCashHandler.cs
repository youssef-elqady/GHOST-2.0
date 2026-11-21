using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Common.Result;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Application.Cash;

public enum WithdrawalCategory
{
    Advance = 1,
    Other = 2
}

public sealed record RecordExpenseCommand(
    Guid ShiftId,
    Guid ActorUserId,
    decimal Amount,
    string Category,
    string Reason,
    string? IdempotencyKey);

public sealed record RecordWithdrawalCommand(
    Guid ShiftId,
    Guid ActorUserId,
    decimal Amount,
    WithdrawalCategory Category,
    string Reason,
    Guid? BeneficiaryUserId,
    string? IdempotencyKey);

public sealed record CashMovementSnapshot(
    Guid TransactionId,
    string Type,
    decimal Amount,
    decimal ExpectedCashAfter);

public sealed record CashLedgerEntry(
    Guid Id,
    string Type,
    string Direction,
    decimal Amount,
    string? Category,
    string? Reason,
    Guid CreatedById,
    DateTimeOffset CreatedAt);

public sealed record ShiftCashSummary(
    Guid ShiftId,
    string Status,
    decimal OpeningCash,
    decimal TotalIn,
    decimal TotalOut,
    decimal ExpectedCash,
    IReadOnlyCollection<CashLedgerEntry> Entries);

/// <summary>
/// Cash movements on a shift's ledger (SRS FR-14, FR-15, BD-08, BD-14). Authorization and
/// shift ownership are checked by the caller; this handler only enforces business rules.
/// </summary>
public sealed class ShiftCashHandler
{
    private readonly IShiftRepository _shiftRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ShiftCashHandler(IShiftRepository shiftRepository, IUnitOfWork unitOfWork)
    {
        _shiftRepository = shiftRepository;
        _unitOfWork = unitOfWork;
    }

    public Task<Result<CashMovementSnapshot>> RecordExpenseAsync(
        RecordExpenseCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Category))
            return Task.FromResult(Result<CashMovementSnapshot>.Failure("An expense category is required."));

        return RecordAsync(
            command.ShiftId,
            shift => shift.RecordCash(
                CashTransactionType.Expense,
                CashDirection.Out,
                command.Amount,
                command.ActorUserId,
                command.Reason,
                command.Category,
                idempotencyKey: command.IdempotencyKey),
            cancellationToken);
    }

    public Task<Result<CashMovementSnapshot>> RecordWithdrawalAsync(
        RecordWithdrawalCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(command.Category))
            return Task.FromResult(Result<CashMovementSnapshot>.Failure("Withdrawal category is invalid."));

        if (command.Category == WithdrawalCategory.Advance &&
            (command.BeneficiaryUserId is null || command.BeneficiaryUserId == Guid.Empty))
        {
            return Task.FromResult(Result<CashMovementSnapshot>.Failure(
                "An advance requires the staff member who receives it."));
        }

        return RecordAsync(
            command.ShiftId,
            shift => shift.RecordCash(
                CashTransactionType.Withdrawal,
                CashDirection.Out,
                command.Amount,
                command.ActorUserId,
                command.Reason,
                command.Category.ToString(),
                command.Category == WithdrawalCategory.Advance ? command.BeneficiaryUserId : null,
                idempotencyKey: command.IdempotencyKey),
            cancellationToken);
    }

    public async Task<Result<ShiftCashSummary>> GetSummaryAsync(
        Guid shiftId,
        CancellationToken cancellationToken = default)
    {
        var shift = await _shiftRepository.GetWithCashAsync(shiftId, cancellationToken);
        if (shift is null)
            return Result<ShiftCashSummary>.Failure("Shift was not found.");

        var entries = shift.CashTransactions
            .OrderBy(item => item.CreatedAt)
            .Select(item => new CashLedgerEntry(
                item.Id,
                item.Type.ToString(),
                item.Direction.ToString(),
                item.Amount,
                item.Category,
                item.Reason,
                item.CreatedById,
                item.CreatedAt))
            .ToArray();

        return Result<ShiftCashSummary>.Success(new ShiftCashSummary(
            shift.Id,
            shift.Status.ToString(),
            shift.OpeningCash,
            shift.CashTransactions.Where(item => item.Direction == CashDirection.In).Sum(item => item.Amount),
            shift.CashTransactions.Where(item => item.Direction == CashDirection.Out).Sum(item => item.Amount),
            shift.ExpectedCash,
            entries));
    }

    private async Task<Result<CashMovementSnapshot>> RecordAsync(
        Guid shiftId,
        Func<Shift, CashTransaction> record,
        CancellationToken cancellationToken)
    {
        if (shiftId == Guid.Empty)
            return Result<CashMovementSnapshot>.Failure("ShiftId is required.");

        var shift = await _shiftRepository.GetWithCashAsync(shiftId, cancellationToken);
        if (shift is null)
            return Result<CashMovementSnapshot>.Failure("Shift was not found.");

        try
        {
            var entry = record(shift);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<CashMovementSnapshot>.Success(new CashMovementSnapshot(
                entry.Id,
                entry.Type.ToString(),
                entry.Amount,
                shift.ExpectedCash));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<CashMovementSnapshot>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<CashMovementSnapshot>.Failure(exception.Message);
        }
    }
}
