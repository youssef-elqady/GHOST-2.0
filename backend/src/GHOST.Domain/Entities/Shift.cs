using GHOST.Domain.Common;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

public sealed class Shift : BaseEntity
{
    private readonly List<CashTransaction> _cashTransactions = new();

    public IReadOnlyCollection<CashTransaction> CashTransactions => _cashTransactions.AsReadOnly();

    public Guid BusinessDayId { get; private set; }

    public Guid StaffUserId { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public decimal OpeningCash { get; private set; }

    public decimal ExpectedCash { get; private set; }

    public decimal? ActualCash { get; private set; }

    public decimal? CashDifference { get; private set; }

    public string? ClosingReason { get; private set; }

    public ShiftStatus Status { get; private set; }

    private Shift()
    {
    }

    internal Shift(Guid businessDayId, Guid staffUserId, decimal openingCash)
    {
        if (businessDayId == Guid.Empty)
            throw new BusinessRuleValidationException("BusinessDay ID cannot be empty.");

        if (staffUserId == Guid.Empty)
            throw new BusinessRuleValidationException("StaffUser ID cannot be empty.");

        if (openingCash < 0)
            throw new BusinessRuleValidationException("Opening cash cannot be negative.");

        BusinessDayId = businessDayId;
        StaffUserId = staffUserId;
        OpeningCash = openingCash;
        ExpectedCash = openingCash;
        Status = ShiftStatus.Open;
        OpenedAt = DateTimeOffset.UtcNow;

        _cashTransactions.Add(CashTransaction.Create(
            Id,
            CashTransactionType.OpeningBalance,
            CashDirection.In,
            openingCash,
            staffUserId,
            reason: null,
            category: null,
            beneficiaryUserId: null,
            sourceType: null,
            sourceId: null,
            idempotencyKey: null,
            OpenedAt));
    }

    /// <summary>
    /// Appends a cash movement to this shift's ledger and moves <see cref="ExpectedCash"/> by
    /// the signed amount. Expected cash is never set directly. Re-sending the same
    /// idempotency key returns the original entry without a second movement.
    /// </summary>
    public CashTransaction RecordCash(
        CashTransactionType type,
        CashDirection direction,
        decimal amount,
        Guid createdById,
        string? reason = null,
        string? category = null,
        Guid? beneficiaryUserId = null,
        string? sourceType = null,
        Guid? sourceId = null,
        string? idempotencyKey = null)
    {
        if (Status != ShiftStatus.Open)
            throw new BusinessRuleValidationException("Cannot record cash on a closed shift.");

        if (type == CashTransactionType.OpeningBalance)
            throw new BusinessRuleValidationException("The opening balance is recorded when the shift opens.");

        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (key is not null)
        {
            var existing = _cashTransactions.FirstOrDefault(item => item.IdempotencyKey == key);
            if (existing is not null)
            {
                if (existing.Type != type || existing.Direction != direction || existing.Amount != amount)
                    throw new BusinessRuleValidationException(
                        "This idempotency key was already used for a different cash movement.");

                return existing;
            }
        }

        var entry = CashTransaction.Create(
            Id, type, direction, amount, createdById, reason, category,
            beneficiaryUserId, sourceType, sourceId, key, DateTimeOffset.UtcNow);

        var newExpected = ExpectedCash + entry.SignedAmount;
        if (newExpected < 0)
            throw new BusinessRuleValidationException(
                $"The drawer does not hold enough cash for this movement (available: {ExpectedCash:N2}).");

        _cashTransactions.Add(entry);
        ExpectedCash = newExpected;
        MarkAsUpdated();
        return entry;
    }

    public void Close(decimal actualCash, string? closingReason)
    {
        if (Status != ShiftStatus.Open)
        {
            throw new BusinessRuleValidationException("Shift is already closed.");
        }

        if (actualCash < 0)
        {
            throw new BusinessRuleValidationException("Actual cash cannot be negative.");
        }

        var difference = actualCash - ExpectedCash;

        if (difference != 0 && string.IsNullOrWhiteSpace(closingReason))
        {
            throw new BusinessRuleValidationException(
                $"A closing reason is required when there is a cash difference (Difference: {difference:N2}).");
        }

        ActualCash = actualCash;
        CashDifference = difference;
        ClosingReason = closingReason?.Trim();
        Status = ShiftStatus.Closed;
        ClosedAt = DateTimeOffset.UtcNow;
        MarkAsUpdated();
    }
}