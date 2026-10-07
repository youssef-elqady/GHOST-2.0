using GHOST.Domain.Common;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

public sealed class Shift : BaseEntity
{
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
    }

    public void UpdateExpectedCash(decimal newExpectedCash)
    {
        if (Status != ShiftStatus.Open)
        {
            throw new BusinessRuleValidationException("Cannot update expected cash on a closed shift.");
        }

        if (newExpectedCash < 0)
        {
            throw new BusinessRuleValidationException("Expected cash cannot be negative.");
        }

        ExpectedCash = newExpectedCash;
        MarkAsUpdated();
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