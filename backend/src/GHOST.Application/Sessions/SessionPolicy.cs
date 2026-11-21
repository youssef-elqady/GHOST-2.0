namespace GHOST.Application.Sessions;

/// <summary>Operational session rules. Defaults follow SRS BD-03 (CancelWindowSeconds = 300).</summary>
public sealed record SessionPolicy
{
    public const string SectionName = "Sessions";

    public int CancelWindowSeconds { get; init; } = 300;

    public TimeSpan CancelWindow => TimeSpan.FromSeconds(Math.Max(0, CancelWindowSeconds));
}
