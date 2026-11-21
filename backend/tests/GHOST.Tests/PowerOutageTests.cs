using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Sessions;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Tests;

public sealed class PowerOutageTests
{
    private static Session NewSession() => new(Guid.NewGuid(), Guid.NewGuid(), PlayType.Single, 60m);

    [Fact]
    public void Outage_is_flagged_and_blocks_other_session_actions()
    {
        var s = NewSession();
        var t0 = s.StartedAt;
        s.MarkPowerOutage(t0.AddMinutes(10), t0.AddMinutes(70));

        Assert.True(s.NeedsReview);
        Assert.Equal(t0.AddMinutes(10), s.OutageStartedAt);
        Assert.Throws<BusinessRuleValidationException>(() => s.CompleteSession(Guid.NewGuid()));
        Assert.Throws<BusinessRuleValidationException>(() => s.PauseActiveDevice());
    }

    [Fact]
    public void Outage_time_is_not_billed_when_admin_ends_after_recovery()
    {
        var s = NewSession();
        var t0 = s.StartedAt;
        s.MarkPowerOutage(t0.AddMinutes(10), t0.AddMinutes(70));

        s.ApproveAfterOutage(Guid.NewGuid(), Guid.NewGuid(), t0.AddMinutes(80), t0.AddHours(2));

        // 80 minutes on the clock minus 60 minutes outage = 20 minutes at 60 EGP/h.
        Assert.InRange(s.GetTotalBillableDuration(s.StartedAt.AddDays(1)).TotalSeconds, 1199, 1200.01);
        Assert.Equal(20m, s.GetTotalAmount(s.StartedAt.AddDays(1)));
        Assert.Equal(SessionStatus.Completed, s.Status);
        Assert.False(s.NeedsReview);
        Assert.NotNull(s.ReviewedAt);
    }

    [Fact]
    public void Admin_can_end_at_last_saved_time()
    {
        var s = NewSession();
        var t0 = s.StartedAt;
        s.MarkPowerOutage(t0.AddMinutes(10), t0.AddMinutes(70));

        s.ApproveAfterOutage(Guid.NewGuid(), Guid.NewGuid(), t0.AddMinutes(10), t0.AddHours(2));

        Assert.Equal(10m, s.GetTotalAmount(s.StartedAt.AddDays(1)));
    }

    [Fact]
    public void Admin_end_inside_the_outage_bills_play_up_to_that_time()
    {
        var s = NewSession();
        var t0 = s.StartedAt;
        s.MarkPowerOutage(t0.AddMinutes(10), t0.AddMinutes(70));

        s.ApproveAfterOutage(Guid.NewGuid(), Guid.NewGuid(), t0.AddMinutes(30), t0.AddHours(2));

        Assert.Equal(30m, s.GetTotalAmount(s.StartedAt.AddDays(1)));
    }

    [Fact]
    public void Approve_rejects_future_end_and_unflagged_sessions()
    {
        var s = NewSession();
        var t0 = s.StartedAt;
        Assert.Throws<BusinessRuleValidationException>(
            () => s.ApproveAfterOutage(Guid.NewGuid(), Guid.NewGuid(), t0.AddMinutes(5), t0.AddMinutes(6)));

        s.MarkPowerOutage(t0.AddMinutes(1), t0.AddMinutes(5));
        Assert.Throws<BusinessRuleValidationException>(
            () => s.ApproveAfterOutage(Guid.NewGuid(), Guid.NewGuid(), t0.AddMinutes(9), t0.AddMinutes(6)));
        Assert.Throws<BusinessRuleValidationException>(
            () => s.ApproveAfterOutage(Guid.NewGuid(), Guid.NewGuid(), t0.AddMinutes(-5), t0.AddMinutes(6)));
    }

    [Fact]
    public void Second_outage_keeps_earliest_start_and_adds_another_pause()
    {
        var s = NewSession();
        var t0 = s.StartedAt;
        s.MarkPowerOutage(t0.AddMinutes(10), t0.AddMinutes(20));
        s.MarkPowerOutage(t0.AddMinutes(30), t0.AddMinutes(40));

        Assert.Equal(t0.AddMinutes(10), s.OutageStartedAt);
        Assert.Equal(t0.AddMinutes(40), s.OutageEndedAt);
        s.ApproveAfterOutage(Guid.NewGuid(), Guid.NewGuid(), t0.AddMinutes(50), t0.AddHours(1));
        Assert.Equal(30m, s.GetTotalAmount(s.StartedAt.AddDays(1))); // 50 - 10 - 10
    }

    [Fact]
    public void Paused_session_is_flagged_without_adding_a_second_pause()
    {
        var s = NewSession();
        var t0 = s.StartedAt;
        s.PauseActiveDevice();
        s.MarkPowerOutage(t0.AddMinutes(1), t0.AddMinutes(5));

        Assert.True(s.NeedsReview);
        Assert.Single(s.GetActiveSegment()!.Pauses);
    }

    // ---- handler ----

    private sealed class Outages(DateTimeOffset? beat, params Session[] sessions) : IOutageRepository
    {
        public DateTimeOffset? Beat = beat;
        public Task<DateTimeOffset?> GetHeartbeatAsync(CancellationToken c = default) => Task.FromResult(Beat);
        public Task SaveHeartbeatAsync(DateTimeOffset now, CancellationToken c = default) { Beat = now; return Task.CompletedTask; }
        public Task<IReadOnlyList<Session>> ListActiveSessionsAsync(CancellationToken c = default)
            => Task.FromResult<IReadOnlyList<Session>>(sessions.Where(s => s.Status == SessionStatus.Active).ToList());
        public Task<IReadOnlyList<Session>> ListNeedingReviewAsync(CancellationToken c = default)
            => Task.FromResult<IReadOnlyList<Session>>(sessions.Where(s => s.NeedsReview).ToList());
        public Task<IReadOnlyDictionary<Guid, string>> GetDeviceNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken c = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(i => i, _ => "PS5-1"));
    }

    private sealed class Uow : IUnitOfWork
    {
        public int Saves;
        public Task<int> SaveChangesAsync(CancellationToken c = default) { Saves++; return Task.FromResult(1); }
    }

    private sealed class Audit : IAuditTrail, ISystemAuditTrail
    {
        public List<string> Actions = new();
        public void Record(Guid actorUserId, string action, string entityType, string entityId, string? j = null) => Actions.Add(action);
        public void Record(string action, string entityType, string entityId, string? j = null) => Actions.Add(action);
    }

    private static PowerOutageHandler Handler(Outages o, Uow u, Audit a, ISessionRepository? sessions = null)
        => new(o, sessions!, u, a, a);

    [Fact]
    public async Task Recovery_flags_active_sessions_when_gap_exceeds_threshold_and_is_audited()
    {
        var s = NewSession();
        var beat = s.StartedAt.AddMinutes(5);
        var o = new Outages(beat, s);
        var u = new Uow();
        var a = new Audit();

        var r = await Handler(o, u, a).RecoverAsync(beat.AddHours(1), TimeSpan.FromMinutes(2));

        Assert.True(r.OutageDetected);
        Assert.Equal(1, r.SessionsFlagged);
        Assert.True(s.NeedsReview);
        Assert.Equal(1, u.Saves);
        Assert.Contains("Session.PowerOutageDetected", a.Actions);
        Assert.Contains("System.PowerOutageDetected", a.Actions);
        Assert.Equal(beat.AddHours(1), o.Beat);
    }

    [Fact]
    public async Task Short_restart_is_not_an_outage()
    {
        var s = NewSession();
        var beat = s.StartedAt.AddMinutes(5);
        var o = new Outages(beat, s);
        var u = new Uow();

        var r = await Handler(o, u, new Audit()).RecoverAsync(beat.AddSeconds(40), TimeSpan.FromMinutes(2));

        Assert.False(r.OutageDetected);
        Assert.False(s.NeedsReview);
        Assert.Equal(0, u.Saves);
    }

    [Fact]
    public async Task First_ever_start_just_writes_a_heartbeat()
    {
        var o = new Outages(null);
        var now = DateTimeOffset.UtcNow;
        var r = await Handler(o, new Uow(), new Audit()).RecoverAsync(now, TimeSpan.FromMinutes(2));
        Assert.False(r.OutageDetected);
        Assert.Equal(now, o.Beat);
    }

    [Fact]
    public async Task Recovery_twice_does_not_double_the_outage()
    {
        var s = NewSession();
        var beat = s.StartedAt.AddMinutes(5);
        var o = new Outages(beat, s);
        var h = Handler(o, new Uow(), new Audit());

        await h.RecoverAsync(beat.AddHours(1), TimeSpan.FromMinutes(2));
        await h.RecoverAsync(beat.AddHours(1).AddSeconds(30), TimeSpan.FromMinutes(2));

        Assert.Single(s.GetActiveSegment()!.Pauses);
    }
}
