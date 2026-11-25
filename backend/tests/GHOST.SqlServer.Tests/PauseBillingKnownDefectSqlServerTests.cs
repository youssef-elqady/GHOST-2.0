namespace GHOST.SqlServer.Tests;

/// <summary>
/// KNOWN DEFECTS (review findings C1 and C2). These assert the CORRECT behaviour and are expected to FAIL on the current code.
/// They run in a separate workflow step that does not gate the build, and they are reported as "known defect" in the summary.
/// When the domain decision is made and the code is fixed they should pass; do not delete or weaken them to make them green.
/// </summary>
public sealed class PauseBillingKnownDefectSqlServerTests : IAsyncLifetime
{
    public Task InitializeAsync() => ScratchDb.EnterAsync();
    public Task DisposeAsync() => ScratchDb.LeaveAsync();

    private static readonly TimeSpan Five = TimeSpan.FromHours(5);

    private static async Task<(Guid Id, DateTimeOffset S, DateTimeOffset Now)> NewAsync()
    {
        var (_, _, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var (id, s) = await CiSupport.NewSessionStartedAsync(shiftId, Five);
        return (id, s, s + Five);
    }

    // C1: a replay after a crash has the same start but a later "recovered" time. Overlap must be deducted once (union).
    [ScratchFact]
    public async Task C1_same_start_different_end_deducts_the_union_once()
    {
        var (id, s, now) = await NewAsync();
        await CiSupport.MarkOutageAsync(id, s.AddHours(1), s.AddHours(2));
        await CiSupport.MarkOutageAsync(id, s.AddHours(1), s.AddMinutes(150));
        var r = await CiSupport.ReadAsync(id, now);
        Console.WriteLine($"C1_SAME_START: pauses={r.Pauses} billable={r.Billable} expected={TimeSpan.FromMinutes(210)}");
        Assert.Equal(TimeSpan.FromMinutes(210), r.Billable);
    }

    [ScratchFact]
    public async Task C1_partially_overlapping_outages_deduct_the_union_once()
    {
        var (id, s, now) = await NewAsync();
        await CiSupport.MarkOutageAsync(id, s.AddHours(1), s.AddHours(2));
        await CiSupport.MarkOutageAsync(id, s.AddMinutes(90), s.AddMinutes(150));
        var r = await CiSupport.ReadAsync(id, now);
        Console.WriteLine($"C1_PARTIAL: pauses={r.Pauses} billable={r.Billable} expected={TimeSpan.FromMinutes(210)}");
        Assert.Equal(TimeSpan.FromMinutes(210), r.Billable);
    }

    [ScratchFact]
    public async Task C1_fully_overlapping_outages_deduct_the_larger_once()
    {
        var (id, s, now) = await NewAsync();
        await CiSupport.MarkOutageAsync(id, s.AddHours(1), s.AddHours(3));
        await CiSupport.MarkOutageAsync(id, s.AddMinutes(90), s.AddMinutes(150));
        var r = await CiSupport.ReadAsync(id, now);
        Console.WriteLine($"C1_FULL: pauses={r.Pauses} billable={r.Billable} expected={TimeSpan.FromHours(3)}");
        Assert.Equal(TimeSpan.FromHours(3), r.Billable);
    }

    // C2: Admin sets the real end after two earlier separate outages. Earlier valid outage time must not be deleted.
    [ScratchFact]
    public async Task C2_approval_does_not_delete_earlier_separate_outages()
    {
        var (id, s, _) = await NewAsync();
        await CiSupport.MarkOutageAsync(id, s.AddHours(1), s.AddHours(2));
        await CiSupport.MarkOutageAsync(id, s.AddHours(3), s.AddMinutes(210));
        var end = s.AddHours(4);
        await using (var db = ScratchDb.NewContext())
        {
            var admin = await ScratchDb.SeedUserAsync("adm");
            var sess = await CiSupport.LoadSessionAsync(db, id);
            sess.ApproveAfterOutage(admin, sess.StartShiftId, end, end.AddMinutes(1));
            await db.SaveChangesAsync();
        }
        await using var check = ScratchDb.NewContext();
        var after = await CiSupport.LoadSessionAsync(check, id);
        var pauses = after.Segments.Sum(x => x.Pauses.Count);
        var billable = after.GetTotalBillableDuration(end);
        Console.WriteLine($"C2_APPROVE: pausesLeft={pauses} billable={billable} expected(4h minus 1h30m)={TimeSpan.FromMinutes(150)}");
        Assert.Equal(TimeSpan.FromMinutes(150), billable);
    }
}
