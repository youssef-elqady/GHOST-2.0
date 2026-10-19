using GHOST.Domain.Common;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

public sealed class SessionPause : BaseEntity
{
    public Guid SessionSegmentId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public string? Reason { get; private set; }

    public bool IsActive => EndedAt is null;

    private SessionPause()
    {
    }

    internal SessionPause(Guid segmentId, string? reason)
    {
        if (segmentId == Guid.Empty)
            throw new BusinessRuleValidationException("SessionSegment ID cannot be empty.");

        SessionSegmentId = segmentId;
        StartedAt = DateTimeOffset.UtcNow;
        Reason = reason?.Trim();
    }

    internal void Resume()
    {
        if (!IsActive)
            throw new BusinessRuleValidationException("This pause has already been resumed/ended.");

        EndedAt = DateTimeOffset.UtcNow;
        MarkAsUpdated();
    }

    public TimeSpan GetDuration(DateTimeOffset? calculateUpTo = null)
    {
        var effectiveEnd = calculateUpTo ?? DateTimeOffset.UtcNow;

        // A historical calculation must not subtract a pause that had not
        // started yet at the requested point in time.
        if (effectiveEnd <= StartedAt)
            return TimeSpan.Zero;

        if (EndedAt.HasValue && EndedAt.Value < effectiveEnd)
            effectiveEnd = EndedAt.Value;

        if (effectiveEnd <= StartedAt)
            return TimeSpan.Zero;

        return effectiveEnd - StartedAt;
    }
}
