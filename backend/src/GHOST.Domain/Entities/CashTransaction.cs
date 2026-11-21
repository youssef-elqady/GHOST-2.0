using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

/// <summary>
/// One immutable entry in a shift's cash ledger (SRS ERD 6.2). Entries are only ever
/// created through <see cref="Shift"/>; there is no update or delete path, and the
/// database enforces append-only with a trigger.
/// </summary>
public sealed class CashTransaction
{
    public const int MaxCategoryLength = 50;
    public const int MaxReasonLength = 500;
    public const int MaxIdempotencyKeyLength = 64;

    private static readonly CashTransactionType[] InTypes =
    [
        CashTransactionType.OpeningBalance,
        CashTransactionType.SaleCash,
        CashTransactionType.StockPurchaseReversal,
        CashTransactionType.HandoverIn,
        CashTransactionType.Adjustment
    ];

    private static readonly CashTransactionType[] OutTypes =
    [
        CashTransactionType.Expense,
        CashTransactionType.Withdrawal,
        CashTransactionType.StockPurchase,
        CashTransactionType.HandoverOut,
        CashTransactionType.Refund,
        CashTransactionType.Adjustment
    ];

    private static readonly CashTransactionType[] ReasonRequiredTypes =
    [
        CashTransactionType.Expense,
        CashTransactionType.Withdrawal,
        CashTransactionType.Refund,
        CashTransactionType.Adjustment,
        CashTransactionType.StockPurchaseReversal
    ];

    public Guid Id { get; private set; }

    public Guid ShiftId { get; private set; }

    public CashTransactionType Type { get; private set; }

    public CashDirection Direction { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>Optional classification such as the expense or withdrawal category.</summary>
    public string? Category { get; private set; }

    /// <summary>Beneficiary staff member of a withdrawal (for example an advance).</summary>
    public Guid? BeneficiaryUserId { get; private set; }

    public string? SourceType { get; private set; }

    public Guid? SourceId { get; private set; }

    public string? Reason { get; private set; }

    public Guid CreatedById { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string? IdempotencyKey { get; private set; }

    /// <summary>+Amount for In entries, -Amount for Out entries.</summary>
    public decimal SignedAmount => Direction == CashDirection.In ? Amount : -Amount;

    private CashTransaction()
    {
    }

    internal static CashTransaction Create(
        Guid shiftId,
        CashTransactionType type,
        CashDirection direction,
        decimal amount,
        Guid createdById,
        string? reason,
        string? category,
        Guid? beneficiaryUserId,
        string? sourceType,
        Guid? sourceId,
        string? idempotencyKey,
        DateTimeOffset now)
    {
        if (shiftId == Guid.Empty)
            throw new BusinessRuleValidationException("ShiftId cannot be empty.");

        if (createdById == Guid.Empty)
            throw new BusinessRuleValidationException("CreatedById cannot be empty.");

        if (!Enum.IsDefined(type) || !Enum.IsDefined(direction))
            throw new BusinessRuleValidationException("Cash transaction type or direction is invalid.");

        var allowed = direction == CashDirection.In ? InTypes : OutTypes;
        if (!allowed.Contains(type))
            throw new BusinessRuleValidationException(
                $"{type} cannot be recorded as cash {direction.ToString().ToLowerInvariant()}.");

        if (amount < 0 || (amount == 0 && type != CashTransactionType.OpeningBalance))
            throw new BusinessRuleValidationException("Cash amount must be greater than zero.");

        if (decimal.Round(amount, 2) != amount)
            throw new BusinessRuleValidationException("Cash amount cannot have more than two decimal places.");

        reason = reason?.Trim();
        if (ReasonRequiredTypes.Contains(type) && string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleValidationException($"A reason is required for {type}.");

        if (reason?.Length > MaxReasonLength)
            throw new BusinessRuleValidationException($"Reason cannot exceed {MaxReasonLength} characters.");

        category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (category?.Length > MaxCategoryLength)
            throw new BusinessRuleValidationException($"Category cannot exceed {MaxCategoryLength} characters.");

        idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (idempotencyKey?.Length > MaxIdempotencyKeyLength)
            throw new BusinessRuleValidationException(
                $"Idempotency key cannot exceed {MaxIdempotencyKeyLength} characters.");

        return new CashTransaction
        {
            Id = Guid.NewGuid(),
            ShiftId = shiftId,
            Type = type,
            Direction = direction,
            Amount = amount,
            Category = category,
            BeneficiaryUserId = beneficiaryUserId,
            SourceType = sourceType,
            SourceId = sourceId,
            Reason = reason,
            CreatedById = createdById,
            CreatedAt = now,
            IdempotencyKey = idempotencyKey
        };
    }
}
