using GHOST.Domain.Common;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

public sealed class BusinessDay : BaseEntity
{
    private readonly List<Shift> _shifts = new();

    public DateOnly BusinessDate { get; private set; }

    public bool IsClosed { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public IReadOnlyCollection<Shift> Shifts => _shifts.AsReadOnly();

    private BusinessDay()
    {
    }

    public BusinessDay(DateOnly businessDate)
    {
        BusinessDate = businessDate;
        IsClosed = false;
        OpenedAt = DateTimeOffset.UtcNow;
    }

    public Shift OpenShift(Guid staffUserId, decimal openingCash)
    {
        if (IsClosed)
        {
            throw new BusinessRuleValidationException(
                $"Cannot open a shift in a closed business day ({BusinessDate}).");
        }

        var hasOpenShiftForUser = _shifts.Any(s => s.StaffUserId == staffUserId && s.Status == ShiftStatus.Open);
        if (hasOpenShiftForUser)
        {
            throw new BusinessRuleValidationException(
                "Staff member already has an active open shift in this business day.");
        }

        var shift = new Shift(Id, staffUserId, openingCash);
        _shifts.Add(shift);
        MarkAsUpdated();

        return shift;
    }

    public void Close()
    {
        if (IsClosed)
        {
            throw new BusinessRuleValidationException(
                $"Business day ({BusinessDate}) is already closed.");
        }

        var hasOpenShifts = _shifts.Any(s => s.Status == ShiftStatus.Open);
        if (hasOpenShifts)
        {
            throw new BusinessRuleValidationException(
                "Cannot close business day while there are active open shifts.");
        }

        IsClosed = true;
        ClosedAt = DateTimeOffset.UtcNow;
        MarkAsUpdated();
    }
}