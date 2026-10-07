using GHOST.Domain.Common;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

public sealed class Session : BaseEntity
{
    private readonly List<SessionSegment> _segments = new();

    public Guid StartShiftId { get; private set; }

    public Guid? EndShiftId { get; private set; }

    public SessionStatus Status { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyCollection<SessionSegment> Segments => _segments.AsReadOnly();

    private Session()
    {
    }

    public Session(
        Guid startShiftId,
        Guid initialDeviceId,
        PlayType playType,
        decimal appliedHourlyRate)
    {
        if (startShiftId == Guid.Empty)
            throw new BusinessRuleValidationException("StartShiftId cannot be empty.");

        StartShiftId = startShiftId;
        Status = SessionStatus.Active;

        var firstSegment = new SessionSegment(Id, initialDeviceId, playType, appliedHourlyRate);
        _segments.Add(firstSegment);
    }

    public SessionSegment? GetActiveSegment()
    {
        return _segments.FirstOrDefault(s => s.IsActive);
    }

    public void TransferToDevice(Guid newDeviceId, PlayType playType, decimal appliedHourlyRate)
    {
        EnsureSessionIsActive();

        var activeSegment = GetActiveSegment();
        if (activeSegment is null)
            throw new BusinessRuleValidationException("No active segment to transfer from.");

        if (activeSegment.DeviceId == newDeviceId)
            throw new BusinessRuleValidationException("Cannot transfer to the same device.");

        activeSegment.End();

        var newSegment = new SessionSegment(Id, newDeviceId, playType, appliedHourlyRate);
        _segments.Add(newSegment);

        MarkAsUpdated();
    }

    public void PauseActiveDevice(string? reason = null)
    {
        EnsureSessionIsActive();

        var activeSegment = GetActiveSegment();
        if (activeSegment is null)
            throw new BusinessRuleValidationException("No active segment to pause.");

        activeSegment.Pause(reason);
        MarkAsUpdated();
    }

    public void ResumeActiveDevice()
    {
        EnsureSessionIsActive();

        var activeSegment = GetActiveSegment();
        if (activeSegment is null)
            throw new BusinessRuleValidationException("No active segment to resume.");

        activeSegment.Resume();
        MarkAsUpdated();
    }

    public void CompleteSession(Guid endShiftId)
    {
        EnsureSessionIsActive();

        if (endShiftId == Guid.Empty)
            throw new BusinessRuleValidationException("EndShiftId cannot be empty when completing a session.");

        var activeSegment = GetActiveSegment();
        activeSegment?.End();

        Status = SessionStatus.Completed;
        EndShiftId = endShiftId;
        MarkAsUpdated();
    }

    public void CancelSession(Guid endShiftId, string reason)
    {
        EnsureSessionIsActive();

        if (endShiftId == Guid.Empty)
            throw new BusinessRuleValidationException("EndShiftId cannot be empty when cancelling a session.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleValidationException("Cancellation reason is required.");

        var activeSegment = GetActiveSegment();
        activeSegment?.End();

        Status = SessionStatus.Cancelled;
        EndShiftId = endShiftId;
        CancellationReason = reason.Trim();
        CancelledAt = DateTimeOffset.UtcNow;
        MarkAsUpdated();
    }

    public TimeSpan GetTotalBillableDuration(DateTimeOffset? calculateUpTo = null)
    {
        var totalTicks = _segments.Sum(s => s.GetBillableDuration(calculateUpTo).Ticks);
        return TimeSpan.FromTicks(totalTicks);
    }

    public bool IsWithinGracePeriod(TimeSpan gracePeriod)
    {
        if (gracePeriod < TimeSpan.Zero)
            throw new BusinessRuleValidationException("Grace period cannot be negative.");

        return (DateTimeOffset.UtcNow - CreatedAt) <= gracePeriod;
    }

    private void EnsureSessionIsActive()
    {
        if (Status != SessionStatus.Active)
        {
            throw new BusinessRuleValidationException($"Cannot perform this action because session is {Status}.");
        }
    }
}