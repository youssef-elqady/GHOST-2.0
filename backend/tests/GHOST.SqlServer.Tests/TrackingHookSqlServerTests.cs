using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GHOST.SqlServer.Tests;

/// <summary>Commit 92dddb8: the ChangeTracker.Tracked hook against a real SQL Server (INSERT versus UPDATE ... 0 rows).</summary>
public sealed class TrackingHookSqlServerTests : IAsyncLifetime
{
    public Task InitializeAsync() => ScratchDb.EnterAsync();
    public Task DisposeAsync() => ScratchDb.LeaveAsync();

    [ScratchFact]
    public async Task Pause_added_to_a_loaded_session_is_inserted()
    {
        var (_, _, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var (id, _) = await CiSupport.NewSessionStartedAsync(shiftId, TimeSpan.FromMinutes(30));
        await using (var db = ScratchDb.NewContext())
        {
            var s = await CiSupport.LoadSessionAsync(db, id);
            s.PauseActiveDevice("ci");
            await db.SaveChangesAsync();
        }
        await using var check = ScratchDb.NewContext();
        Assert.Equal(1, await check.SessionPauses.CountAsync(p => p.SessionSegmentId == check.SessionSegments.Where(x => x.SessionId == id).Select(x => x.Id).First()));
    }

    [ScratchFact]
    public async Task Transfer_of_a_loaded_session_inserts_the_new_segment_and_keeps_the_old_one()
    {
        var (_, _, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var (id, _) = await CiSupport.NewSessionStartedAsync(shiftId, TimeSpan.FromMinutes(30));
        await using (var db = ScratchDb.NewContext())
        {
            var other = new Device("CI2-" + Guid.NewGuid().ToString("N")[..8], "PS5");
            db.Devices.Add(other);
            await db.SaveChangesAsync();
            var s = await CiSupport.LoadSessionAsync(db, id);
            s.TransferToDevice(other.Id, PlayType.Single, 60m);
            await db.SaveChangesAsync();
        }
        await using var check = ScratchDb.NewContext();
        var segs = await check.SessionSegments.Where(x => x.SessionId == id).ToListAsync();
        Assert.Equal(2, segs.Count);
        Assert.Equal(1, segs.Count(x => x.EndedAt != null));
    }

    [ScratchFact]
    public async Task Shift_added_to_a_loaded_business_day_is_inserted()
    {
        var (_, _, _) = await ScratchDb.SeedOpenShiftAsync();
        await using var check0 = ScratchDb.NewContext();
        var day = await check0.BusinessDays.Include(d => d.Shifts).SingleAsync(d => !d.IsClosed);
        var before = day.Shifts.Count;
        // The schema allows one open shift, so close the open one first and then open the second on the loaded day.
        foreach (var sh in day.Shifts.Where(x => x.Status == ShiftStatus.Open)) sh.Close(sh.ExpectedCash, "ci");
        await check0.SaveChangesAsync();
        var newOwner = await ScratchDb.SeedUserAsync("stf2");
        await using (var db = ScratchDb.NewContext())
        {
            var loaded = await db.BusinessDays.Include(d => d.Shifts).SingleAsync(d => d.Id == day.Id);
            loaded.OpenShift(newOwner, 10m);
            await db.SaveChangesAsync();
        }
        await using var check = ScratchDb.NewContext();
        Assert.Equal(before + 1, await check.Shifts.CountAsync(x => x.BusinessDayId == day.Id));
    }

    [ScratchFact]
    public async Task Changing_a_loaded_parent_updates_it_and_does_not_insert_a_copy()
    {
        var (_, _, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var (id, _) = await CiSupport.NewSessionStartedAsync(shiftId, TimeSpan.FromMinutes(30));
        await using (var db = ScratchDb.NewContext())
        {
            var s = await CiSupport.LoadSessionAsync(db, id);
            s.CompleteSession(shiftId);
            Assert.Equal(EntityState.Modified, db.Entry(s).State);
            Assert.All(s.Segments, seg => Assert.NotEqual(EntityState.Added, db.Entry(seg).State));
            await db.SaveChangesAsync();
        }
        await using var check = ScratchDb.NewContext();
        Assert.Equal(1, await check.Sessions.CountAsync(x => x.Id == id));
        Assert.Equal(SessionStatus.Completed, await check.Sessions.Where(x => x.Id == id).Select(x => x.Status).SingleAsync());
    }

    /// <summary>
    /// The hook is global. Spec question: what does Update/Attach of an existing detached graph do now? The test records the
    /// outcome and only asserts the safety property: no duplicate row and no silent data loss. It passes when EF either
    /// updates the existing rows or refuses; it fails if a second copy of the session appears.
    /// </summary>
    [ScratchFact]
    public async Task Update_of_an_existing_detached_session_never_creates_a_duplicate_row()
    {
        var (_, _, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var (id, _) = await CiSupport.NewSessionStartedAsync(shiftId, TimeSpan.FromMinutes(30));
        Session detached;
        await using (var db = ScratchDb.NewContext())
            detached = await db.Sessions.AsNoTracking().Include(s => s.Segments).SingleAsync(s => s.Id == id);

        string outcome;
        await using (var db = ScratchDb.NewContext())
        {
            db.Sessions.Update(detached);
            var parentState = db.Entry(detached).State;
            var segmentStates = detached.Segments.Select(x => db.Entry(x).State).ToList();
            try
            {
                await db.SaveChangesAsync();
                outcome = $"saved; parent={parentState}; segments={string.Join(",", segmentStates)}";
            }
            catch (Exception e) when (e is DbUpdateException or InvalidOperationException)
            {
                outcome = $"refused ({e.GetType().Name}); parent={parentState}; segments={string.Join(",", segmentStates)}";
            }
        }
        Console.WriteLine("TRACKING_UPDATE_DETACHED_OUTCOME: " + outcome);
        await using var check = ScratchDb.NewContext();
        Assert.Equal(1, await check.Sessions.CountAsync(x => x.Id == id));
        Assert.Equal(1, await check.SessionSegments.CountAsync(x => x.SessionId == id));
    }
}
