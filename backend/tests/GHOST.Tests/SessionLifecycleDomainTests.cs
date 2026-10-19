using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Tests;

public sealed class SessionLifecycleDomainTests
{
    [Fact]
    public void PauseActiveDevice_CreatesAnOpenPause_AndExcludesPausedTimeFromBilling()
    {
        var session = CreateSession();
        var segment = Assert.Single(session.Segments);

        session.PauseActiveDevice();

        var pause = Assert.Single(segment.Pauses);
        Assert.True(pause.IsActive);

        var calculationTime = pause.StartedAt.AddSeconds(2);
        var billableDuration = segment.GetBillableDuration(calculationTime);

        Assert.Equal(pause.StartedAt - segment.StartedAt, billableDuration);
    }

    [Fact]
    public void ResumeActiveDevice_ClosesTheCurrentPause()
    {
        var session = CreateSession();
        session.PauseActiveDevice();

        var pause = Assert.Single(Assert.Single(session.Segments).Pauses);
        Thread.Sleep(1);

        session.ResumeActiveDevice();

        Assert.False(pause.IsActive);
        Assert.Null(session.GetActiveSegment()!.Pauses.Single().EndedAt is null
            ? session.GetActiveSegment()!.Pauses.Single().EndedAt
            : null);
    }

    [Fact]
    public void PauseActiveDevice_RejectsPausingAnAlreadyPausedSegment()
    {
        var session = CreateSession();
        session.PauseActiveDevice();

        Assert.Throws<BusinessRuleValidationException>(
            () => session.PauseActiveDevice());
    }

    [Fact]
    public void ResumeActiveDevice_RejectsA segmentThatIsNotPaused()
    {
        var session = CreateSession();

        Assert.Throws<BusinessRuleValidationException>(
            () => session.ResumeActiveDevice());
    }

    [Fact]
    public void CompleteSession_EndsTheSegment_AndClosesAnOpenPause()
    {
        var session = CreateSession();
        session.PauseActiveDevice();

        var segment = Assert.Single(session.Segments);
        var pause = Assert.Single(segment.Pauses);
        var endShiftId = Guid.NewGuid();

        Thread.Sleep(1);
        session.CompleteSession(endShiftId);

        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.Equal(endShiftId, session.EndShiftId);
        Assert.Null(session.GetActiveSegment());
        Assert.False(segment.IsActive);
        Assert.False(pause.IsActive);
    }

    [Fact]
    public void TransferToDevice_EndsOldSegment_AndKeepsTheAppliedRatePerSegment()
    {
        var session = CreateSession();
        var oldDeviceId = Assert.Single(session.Segments).DeviceId;
        var newDeviceId = Guid.NewGuid();

        Thread.Sleep(1);
        session.TransferToDevice(newDeviceId, PlayType.Multi, 40m);

        var segments = session.Segments.ToArray();

        Assert.Equal(2, segments.Length);
        Assert.Equal(oldDeviceId, segments[0].DeviceId);
        Assert.False(segments[0].IsActive);
        Assert.Equal(60m, segments[0].AppliedHourlyRate);
        Assert.Equal(newDeviceId, segments[1].DeviceId);
        Assert.True(segments[1].IsActive);
        Assert.Equal(40m, segments[1].AppliedHourlyRate);
        Assert.Equal(SessionStatus.Active, session.Status);
    }

    [Fact]
    public void TransferToDevice_RejectsTransferringToTheSameDevice()
    {
        var session = CreateSession();
        var deviceId = Assert.Single(session.Segments).DeviceId;

        Assert.Throws<BusinessRuleValidationException>(
            () => session.TransferToDevice(deviceId, PlayType.Multi, 40m));
    }

    private static Session CreateSession() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PlayType.Single,
            60m);
}
