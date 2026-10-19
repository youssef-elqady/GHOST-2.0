using GHOST.Domain.Entities;
using GHOST.Domain.Enums;

namespace GHOST.Tests;

public sealed class SessionSegmentTests
{
    [Fact]
    public void GetBillableDuration_ReturnsZero_WhenSegmentStartsAfterCalculationTime()
    {
        var session = new Session(Guid.NewGuid(), Guid.NewGuid(), PlayType.Single, 60m);
        session.TransferToDevice(Guid.NewGuid(), PlayType.Multi, 40m);

        var laterSegment = session.Segments.OrderBy(s => s.StartedAt).Last();

        var duration = laterSegment.GetBillableDuration(laterSegment.StartedAt.AddTicks(-1));

        Assert.Equal(TimeSpan.Zero, duration);
    }
}
