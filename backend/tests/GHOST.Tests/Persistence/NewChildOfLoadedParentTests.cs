using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Tests.Persistence;

/// <summary>
/// SC09: a new child (pause, segment, shift) added to a parent that was loaded from the database carries its Guid key from
/// the constructor. Without the fix EF treats it as an existing row and sends UPDATE, which affects 0 rows.
/// These run on the EF in-memory provider, which raises the same DbUpdateConcurrencyException for that case. They prove
/// the state EF assigns. They do not prove the SQL that SQL Server runs.
/// </summary>
public sealed class NewChildOfLoadedParentTests
{
    private static readonly Guid Staff = Guid.NewGuid();

    private static GHOSTDbContext NewDb(string name)
        => new(new DbContextOptionsBuilder<GHOSTDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<Guid> SeedActiveSessionAsync(string name)
    {
        await using var db = NewDb(name);
        var shift = new BusinessDay(new DateOnly(2026, 11, 25)).OpenShift(Staff, 100m);
        var session = new Session(shift.Id, Guid.NewGuid(), PlayType.Single, 60m);
        db.Shifts.Add(shift);
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private static Task<Session> LoadAsync(GHOSTDbContext db, Guid id)
        => db.Sessions.Include(s => s.Segments).ThenInclude(g => g.Pauses).SingleAsync(s => s.Id == id);

    [Fact]
    public async Task Pause_OnLoadedSession_InsertsThePause()
    {
        var name = Guid.NewGuid().ToString("N");
        var id = await SeedActiveSessionAsync(name);

        await using (var db = NewDb(name))
        {
            var session = await LoadAsync(db, id);
            session.PauseActiveDevice("break");
            await db.SaveChangesAsync();
        }

        await using var check = NewDb(name);
        Assert.Equal(1, await check.SessionPauses.CountAsync());
    }

    [Fact]
    public async Task Transfer_OnLoadedSession_InsertsTheNewSegment_AndKeepsTheOldOne()
    {
        var name = Guid.NewGuid().ToString("N");
        var id = await SeedActiveSessionAsync(name);

        await using (var db = NewDb(name))
        {
            var session = await LoadAsync(db, id);
            session.TransferToDevice(Guid.NewGuid(), PlayType.Single, 60m);
            await db.SaveChangesAsync();
        }

        await using var check = NewDb(name);
        Assert.Equal(2, await check.SessionSegments.CountAsync());
    }

    [Fact]
    public async Task PowerOutageMark_OnLoadedSession_InsertsOneClosedPause_AndASecondMarkAddsNone()
    {
        var name = Guid.NewGuid().ToString("N");
        var id = await SeedActiveSessionAsync(name);
        var lastSaved = DateTimeOffset.UtcNow.AddMinutes(-1);
        var recovered = DateTimeOffset.UtcNow.AddMinutes(1);

        await using (var db = NewDb(name))
        {
            var session = await LoadAsync(db, id);
            session.MarkPowerOutage(lastSaved, recovered);
            await db.SaveChangesAsync();
        }

        // A second recovery run for the same outage must not add another pause.
        await using (var db = NewDb(name))
        {
            var session = await LoadAsync(db, id);
            session.MarkPowerOutage(lastSaved, recovered);
            await db.SaveChangesAsync();
        }

        await using var check = NewDb(name);
        Assert.Equal(1, await check.SessionPauses.CountAsync());
        Assert.True((await check.Sessions.SingleAsync()).NeedsReview);
    }

    [Fact]
    public async Task OpenShift_OnLoadedBusinessDay_InsertsTheShift()
    {
        var name = Guid.NewGuid().ToString("N");
        await using (var db = NewDb(name))
        {
            db.BusinessDays.Add(new BusinessDay(new DateOnly(2026, 11, 26)));
            await db.SaveChangesAsync();
        }

        await using (var db = NewDb(name))
        {
            var day = await db.BusinessDays.Include(d => d.Shifts).SingleAsync();
            day.OpenShift(Staff, 50m);
            await db.SaveChangesAsync();
        }

        await using var check = NewDb(name);
        Assert.Equal(1, await check.Shifts.CountAsync());
    }

    [Fact]
    public async Task LoadedEntity_ChangedInPlace_StaysModified_AndIsUpdatedNotInserted()
    {
        var name = Guid.NewGuid().ToString("N");
        var id = await SeedActiveSessionAsync(name);

        await using (var db = NewDb(name))
        {
            var session = await LoadAsync(db, id);
            session.CompleteSession(session.StartShiftId);
            Assert.Equal(EntityState.Modified, db.Entry(session).State);
            await db.SaveChangesAsync();
        }

        await using var check = NewDb(name);
        Assert.Equal(1, await check.Sessions.CountAsync());
        Assert.Equal(SessionStatus.Completed, (await check.Sessions.SingleAsync()).Status);
    }

    [Fact]
    public async Task NewChild_IsAddedState_AndLoadedParentIsNotRewrittenAsNew()
    {
        var name = Guid.NewGuid().ToString("N");
        var id = await SeedActiveSessionAsync(name);

        await using var db = NewDb(name);
        var session = await LoadAsync(db, id);
        var firstSegment = session.Segments.Single();
        session.TransferToDevice(Guid.NewGuid(), PlayType.Single, 60m);
        db.ChangeTracker.DetectChanges();

        var newSegment = session.Segments.Single(s => s.Id != firstSegment.Id);
        Assert.Equal(EntityState.Added, db.Entry(newSegment).State);
        Assert.NotEqual(EntityState.Added, db.Entry(firstSegment).State);
        Assert.NotEqual(EntityState.Added, db.Entry(session).State);
    }
}

public sealed class OutageReplayBillingTests
{
    [Fact]
    public void SameOutageWindowReplayed_DoesNotDeductTheOutageTwice()
    {
        var session = new Session(Guid.NewGuid(), Guid.NewGuid(), PlayType.Single, 60m);
        var start = DateTimeOffset.UtcNow.AddMinutes(1);
        var recovered = DateTimeOffset.UtcNow.AddMinutes(21);
        var upTo = DateTimeOffset.UtcNow.AddMinutes(41);

        session.MarkPowerOutage(start, recovered);
        var once = session.GetTotalBillableDuration(upTo);
        session.MarkPowerOutage(start, recovered);
        var twice = session.GetTotalBillableDuration(upTo);

        Assert.Single(session.GetActiveSegment()!.Pauses);
        Assert.Equal(once, twice);
        Assert.True(twice > TimeSpan.FromMinutes(19) && twice < TimeSpan.FromMinutes(22)); // 41 minutes minus ONE 20 minute pause
    }
}
