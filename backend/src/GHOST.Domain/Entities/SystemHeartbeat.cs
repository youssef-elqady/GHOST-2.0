namespace GHOST.Domain.Entities;

/// <summary>
/// Single-row record of the last moment the API was known to be running. After a power cut or crash the gap
/// between this time and the next start is the outage window.
/// </summary>
public sealed class SystemHeartbeat
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;

    public DateTimeOffset BeatAt { get; private set; }

    private SystemHeartbeat()
    {
    }

    public SystemHeartbeat(DateTimeOffset beatAt) => BeatAt = beatAt;

    public void Beat(DateTimeOffset at)
    {
        if (at > BeatAt)
            BeatAt = at;
    }
}
