using GHOST.Domain.Common;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;
using GHOST.Domain.Services;

namespace GHOST.Domain.Entities;

public sealed class Session : BaseEntity
{
    private readonly List<SessionSegment> _segments = new();

    public Guid StartShiftId { get; private set; }

    public Guid? EndShiftId { get; private set; }

    public SessionStatus Status { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>True when an Admin cancelled the session after the free cancellation window.</summary>
    public bool IsVoided { get; private set; }

    public DateTimeOffset StartedAt => CreatedAt;

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

    /// <summary>
    /// Cancels (inside the window, any operator) or voids (after the window, Admin only)
    /// a session according to SRS BD-03. The charge becomes zero; time is kept for analytics.
    /// </summary>
    public void CancelOrVoid(
        Guid endShiftId,
        SessionCancelReason reason,
        string? details,
        TimeSpan cancelWindow,
        bool actorIsAdmin,
        DateTimeOffset? now = null)
    {
        EnsureSessionIsActive();

        if (!Enum.IsDefined(reason))
            throw new BusinessRuleValidationException("Cancellation reason is invalid.");

        if (cancelWindow < TimeSpan.Zero)
            throw new BusinessRuleValidationException("Cancel window cannot be negative.");

        var at = now ?? DateTimeOffset.UtcNow;
        var withinWindow = at - StartedAt <= cancelWindow;
        var text = details?.Trim();

        if (!withinWindow && !actorIsAdmin)
            throw new BusinessRuleValidationException(
                "The cancellation window has passed. End the session and charge the played time, or ask an admin to void it.");

        if ((reason == SessionCancelReason.Other || !withinWindow) && string.IsNullOrWhiteSpace(text))
            throw new BusinessRuleValidationException(
                withinWindow
                    ? "Details are required when the reason is Other."
                    : "A written reason is required to void a session.");

        CancelSession(
            endShiftId,
            string.IsNullOrEmpty(text) ? reason.ToString() : $"{reason}: {text}");
        IsVoided = !withinWindow;
    }

    public TimeSpan GetTotalBillableDuration(DateTimeOffset? calculateUpTo = null)
    {
        var calculationTime = calculateUpTo ?? DateTimeOffset.UtcNow;
        var totalTicks = _segments.Sum(s => s.GetBillableDuration(calculationTime).Ticks);
        return TimeSpan.FromTicks(totalTicks);
    }

    public decimal GetTotalAmount(DateTimeOffset? calculateUpTo = null)
    {
        // A cancelled or voided session is never charged (BD-03, ERD 6.3.1).
        if (Status == SessionStatus.Cancelled)
            return 0m;

        // Use one timestamp for every segment so an active session is priced
        // against a consistent point in time.
        var calculationTime = calculateUpTo ?? DateTimeOffset.UtcNow;

        var billableSegments = _segments.Select(segment =>
            (segment.GetBillableDuration(calculationTime), segment.AppliedHourlyRate));

        return SessionBillingCalculator.CalculateTotalAmount(billableSegments);
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
