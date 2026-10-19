using GHOST.Domain.Entities;
using GHOST.Domain.Enums;

namespace GHOST.Tests;

public sealed class SessionPauseTests
{
    [Fact]
    public void GetDuration_ReturnsZero_WhenCalculationTimeIsBeforePauseStarts()
    {
        var session = CreateSession();
        session.PauseActiveDevice();
        var pause = Assert.Single(Assert.Single(session.Segments).Pauses);

        var duration = pause.GetDuration(pause.StartedAt.AddTicks(-1));

        Assert.Equal(TimeSpan.Zero, duration);
    }

    [Fact]
    public void GetDuration_ClampsEndedPauseToHistoricalCalculationTime()
    {
        var session = CreateSession();
        session.PauseActiveDevice();
        var pause = Assert.Single(Assert.Single(session.Segments).Pauses);

        session.ResumeActiveDevice();

        var duration = pause.GetDuration(pause.StartedAt.AddTicks(1));

        Assert.Equal(TimeSpan.FromTicks(1), duration);
    }

    private static Session CreateSession() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PlayType.Single,
            60m);
}
