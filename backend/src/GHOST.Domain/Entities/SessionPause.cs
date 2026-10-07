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
        DateTimeOffset effectiveEnd;

        if (EndedAt.HasValue)
        {
            effectiveEnd = EndedAt.Value;
        }
        else
        {
            effectiveEnd = calculateUpTo ?? DateTimeOffset.UtcNow;
        }

        if (effectiveEnd < StartedAt)
        {
            throw new BusinessRuleValidationException("Calculation time cannot be earlier than pause start time.");
        }

        return effectiveEnd - StartedAt;
    }
}