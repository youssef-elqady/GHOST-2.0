using GHOST.Domain.Common;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

public sealed class SessionSegment : BaseEntity
{
    private readonly List<SessionPause> _pauses = new();

    public Guid SessionId { get; private set; }
    public Guid DeviceId { get; private set; }
    public PlayType PlayType { get; private set; }
    public decimal AppliedHourlyRate { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public bool IsActive => EndedAt is null;
    public IReadOnlyCollection<SessionPause> Pauses => _pauses.AsReadOnly();

    private SessionSegment()
    {
    }

    internal SessionSegment(Guid sessionId, Guid deviceId, PlayType playType, decimal appliedHourlyRate)
    {
        if (sessionId == Guid.Empty)
            throw new BusinessRuleValidationException("Session ID cannot be empty.");
        if (deviceId == Guid.Empty)
            throw new BusinessRuleValidationException("Device ID cannot be empty.");
        if (appliedHourlyRate <= 0)
            throw new BusinessRuleValidationException("Applied hourly rate must be greater than zero.");

        SessionId = sessionId;
        DeviceId = deviceId;
        PlayType = playType;
        AppliedHourlyRate = appliedHourlyRate;
        StartedAt = DateTimeOffset.UtcNow;
    }

    internal void End()
    {
        if (!IsActive)
            throw new BusinessRuleValidationException("Segment is already ended.");

        var activePause = _pauses.FirstOrDefault(p => p.IsActive);
        activePause?.Resume();

        EndedAt = DateTimeOffset.UtcNow;
        MarkAsUpdated();
    }

    internal void Pause(string? reason)
    {
        if (!IsActive)
            throw new BusinessRuleValidationException("Cannot pause an ended segment.");
        if (_pauses.Any(p => p.IsActive))
            throw new BusinessRuleValidationException("Segment is already paused.");

        _pauses.Add(new SessionPause(Id, reason));
        MarkAsUpdated();
    }

    internal bool IsPaused => _pauses.Any(p => p.IsActive);

    /// <summary>Records time the device was not running (power outage) as an already-closed pause, so it is not billed.</summary>
    internal void AddClosedPause(string reason, DateTimeOffset startedAt, DateTimeOffset endedAt)
    {
        var start = startedAt < StartedAt ? StartedAt : startedAt;
        if (endedAt <= start)
            return;

        // The same outage window can be replayed (a crash between the session save and the heartbeat save makes the next
        // start use the same window). Never store the same closed pause twice: it would be deducted from billing twice.
        if (_pauses.Any(p => p.EndedAt == endedAt && p.StartedAt == start && p.Reason == reason))
            return;

        _pauses.Add(new SessionPause(Id, reason, start, endedAt));
        MarkAsUpdated();
    }

    /// <summary>
    /// Ends the segment at an explicit time (admin-approved end after an outage). Pause records are never deleted or
    /// rewritten: billing clips every pause to the segment end, so no pause
    /// ever subtract anything after that end. Every pause is deducted only for its overlap with the segment.
    /// </summary>
    internal void EndAt(DateTimeOffset at)
    {
        if (!IsActive)
            throw new BusinessRuleValidationException("Segment is already ended.");
        if (at < StartedAt)
            throw new BusinessRuleValidationException("The end time cannot be before the session segment started.");

        _pauses.FirstOrDefault(p => p.IsActive)?.EndAt(at);

        EndedAt = at;
        MarkAsUpdated();
    }

    internal void Resume()
    {
        if (!IsActive)
            throw new BusinessRuleValidationException("Cannot resume an ended segment.");

        var activePause = _pauses.FirstOrDefault(p => p.IsActive);
        if (activePause is null)
            throw new BusinessRuleValidationException("Segment is not currently paused.");

        activePause.Resume();
        MarkAsUpdated();
    }

    public TimeSpan GetBillableDuration(DateTimeOffset? calculateUpTo = null)
    {
        var end = calculateUpTo ?? DateTimeOffset.UtcNow;

        // A later segment contributes nothing to a historical calculation
        // performed before that segment began (for example, before a transfer).
        if (end <= StartedAt)
            return TimeSpan.Zero;

        if (EndedAt.HasValue && end > EndedAt.Value)
            end = EndedAt.Value;

        var totalDuration = end - StartedAt;
        var totalPausedTimeTicks = _pauses.Sum(p => p.GetDuration(end).Ticks);
        var billableDuration = totalDuration - TimeSpan.FromTicks(totalPausedTimeTicks);

        return billableDuration.Ticks < 0 ? TimeSpan.Zero : billableDuration;
    }
}
