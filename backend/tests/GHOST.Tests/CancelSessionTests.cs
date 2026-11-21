using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Sessions;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Tests;

public sealed class CancelSessionTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(300);

    private static Session NewSession() => new(Guid.NewGuid(), Guid.NewGuid(), PlayType.Single, 60m);

    [Fact]
    public void Cancel_InsideWindow_ByStaff_ZeroChargeNotVoided()
    {
        var session = NewSession();

        session.CancelOrVoid(Guid.NewGuid(), SessionCancelReason.WrongDevice, null, Window, actorIsAdmin: false);

        Assert.Equal(SessionStatus.Cancelled, session.Status);
        Assert.False(session.IsVoided);
        Assert.Equal(0m, session.GetTotalAmount());
        Assert.Equal("WrongDevice", session.CancellationReason);
    }

    [Fact]
    public void Cancel_AfterWindow_ByStaff_IsRejected()
    {
        var session = NewSession();
        var later = session.StartedAt.AddSeconds(301);

        var ex = Assert.Throws<BusinessRuleValidationException>(() => session.CancelOrVoid(
            Guid.NewGuid(), SessionCancelReason.ChangedMind, null, Window, false, later));

        Assert.Contains("window has passed", ex.Message);
        Assert.Equal(SessionStatus.Active, session.Status);
    }

    [Fact]
    public void Cancel_ExactlyAtWindowBoundary_StillFree()
    {
        var session = NewSession();

        session.CancelOrVoid(
            Guid.NewGuid(), SessionCancelReason.ChangedMind, null, Window, false, session.StartedAt.AddSeconds(300));

        Assert.False(session.IsVoided);
    }

    [Fact]
    public void Void_AfterWindow_ByAdmin_RequiresWrittenReason()
    {
        var session = NewSession();
        var later = session.StartedAt.AddMinutes(20);

        Assert.Throws<BusinessRuleValidationException>(() => session.CancelOrVoid(
            Guid.NewGuid(), SessionCancelReason.OperatorError, "  ", Window, true, later));

        session.CancelOrVoid(
            Guid.NewGuid(), SessionCancelReason.OperatorError, "started on wrong console", Window, true, later);

        Assert.True(session.IsVoided);
        Assert.Equal(0m, session.GetTotalAmount());
        Assert.Equal("OperatorError: started on wrong console", session.CancellationReason);
    }

    [Fact]
    public void Cancel_OtherReason_NeedsDetails()
    {
        var session = NewSession();

        Assert.Throws<BusinessRuleValidationException>(() => session.CancelOrVoid(
            Guid.NewGuid(), SessionCancelReason.Other, null, Window, false));
    }

    [Fact]
    public void Cancel_AlreadyCancelledOrInvalidReason_IsRejected()
    {
        var session = NewSession();
        session.CancelOrVoid(Guid.NewGuid(), SessionCancelReason.ChangedMind, null, Window, false);

        Assert.Throws<BusinessRuleValidationException>(() => session.CancelOrVoid(
            Guid.NewGuid(), SessionCancelReason.ChangedMind, null, Window, true));
        Assert.Throws<BusinessRuleValidationException>(() => NewSession().CancelOrVoid(
            Guid.NewGuid(), (SessionCancelReason)99, null, Window, true));
    }

    [Fact]
    public async Task Handler_CancelsFreesDeviceAndStagesAuditRecord()
    {
        var (session, device, shift) = Setup();
        var audit = new RecordingAudit();
        var uow = new CountingUow();
        var handler = new CancelSessionHandler(
            new Repo(session, device, shift), uow, audit, new SessionPolicy());
        var actor = Guid.NewGuid();

        var result = await handler.HandleAsync(new CancelSessionCommand(
            session.Id, shift.Id, actor, false, SessionCancelReason.ChangedMind, null));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0m, result.Value!.Amount);
        Assert.False(result.Value.IsVoided);
        Assert.Equal(DeviceStatus.Available, device.Status);
        Assert.Equal(1, uow.SaveCalls);
        var record = Assert.Single(audit.Records);
        Assert.Equal("Session.Cancelled", record.Action);
        Assert.Equal(actor, record.Actor);
    }

    [Fact]
    public async Task Handler_StaffAfterWindow_FailsAndLeavesEverythingUntouched()
    {
        var (session, device, shift) = Setup();
        var audit = new RecordingAudit();
        var uow = new CountingUow();
        var handler = new CancelSessionHandler(
            new Repo(session, device, shift), uow, audit, new SessionPolicy { CancelWindowSeconds = 0 });
        Thread.Sleep(5);

        var result = await handler.HandleAsync(new CancelSessionCommand(
            session.Id, shift.Id, Guid.NewGuid(), false, SessionCancelReason.ChangedMind, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.Equal(DeviceStatus.Running, device.Status);
        Assert.Empty(audit.Records);
        Assert.Equal(0, uow.SaveCalls);
    }

    [Fact]
    public async Task Handler_AdminAfterWindow_VoidsAndAuditsVoid()
    {
        var (session, device, shift) = Setup();
        var audit = new RecordingAudit();
        var handler = new CancelSessionHandler(
            new Repo(session, device, shift), new CountingUow(), audit, new SessionPolicy { CancelWindowSeconds = 0 });
        Thread.Sleep(5);

        var result = await handler.HandleAsync(new CancelSessionCommand(
            session.Id, shift.Id, Guid.NewGuid(), true, SessionCancelReason.OperatorError, "wrong customer"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.IsVoided);
        Assert.Equal("Session.Voided", Assert.Single(audit.Records).Action);
    }

    [Fact]
    public async Task Handler_RejectsClosedShift()
    {
        var (session, device, shift) = Setup();
        shift.Close(shift.ExpectedCash, null);
        var handler = new CancelSessionHandler(
            new Repo(session, device, shift), new CountingUow(), new RecordingAudit(), new SessionPolicy());

        var result = await handler.HandleAsync(new CancelSessionCommand(
            session.Id, shift.Id, Guid.NewGuid(), true, SessionCancelReason.ChangedMind, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(SessionStatus.Active, session.Status);
    }

    private static (Session, Device, Shift) Setup()
    {
        var device = new Device("PS1", "PlayStation");
        device.SetRate(PlayType.Single, 60m);
        device.MarkAsRunning(PlayType.Single);
        var shift = new BusinessDay(new DateOnly(2026, 11, 21)).OpenShift(Guid.NewGuid(), 100m);
        var session = new Session(shift.Id, device.Id, PlayType.Single, 60m);
        return (session, device, shift);
    }

    private sealed class Repo(Session session, Device device, Shift shift) : ISessionRepository
    {
        public Task<Session?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Session?>(sessionId == session.Id ? session : null);
        public Task<Session?> GetActiveByDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Session?>(session);
        public Task<Device?> GetDeviceForStartAsync(Guid deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Device?>(deviceId == device.Id ? device : null);
        public Task<Shift?> GetShiftAsync(Guid shiftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Shift?>(shiftId == shift.Id ? shift : null);
        public Task AddAsync(Session s, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingAudit : IAuditTrail
    {
        public List<(Guid Actor, string Action)> Records { get; } = [];

        public void Record(Guid actorUserId, string action, string entityType, string entityId, string? newValuesJson = null) =>
            Records.Add((actorUserId, action));
    }

    private sealed class CountingUow : IUnitOfWork
    {
        public int SaveCalls { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(1);
        }
    }
}
