namespace GHOST.SqlServer.Tests;

/// <summary>
/// C2 rule (user decision): the Admin-approved end time is the session end, even inside an outage. Earlier outages stay deducted,
/// the outage covering the approved end does not subtract time before it, a later outage has no effect, no pause row is deleted
/// or changed. Session rate is 60 per hour. Outages: 1h-2h and 3h-3h30 after the start.
/// </summary>
public sealed class OutageApprovalSqlServerTests : IAsyncLifetime
{
    public Task InitializeAsync() => ScratchDb.EnterAsync();
    public Task DisposeAsync() => ScratchDb.LeaveAsync();

    private static async Task<(Guid Id, Guid ShiftId, DateTimeOffset S, List<(DateTimeOffset, DateTimeOffset?)> Before)> SeedAsync(
        params (double From, double To)[] outagesInHours)
    {
        var (_, _, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var (id, s) = await CiSupport.NewSessionStartedAsync(shiftId, TimeSpan.FromHours(8));
        foreach (var (from, to) in outagesInHours)
            await CiSupport.MarkOutageAsync(id, s.AddHours(from), s.AddHours(to));
        await using var db = ScratchDb.NewContext();
        var before = (await CiSupport.LoadSessionAsync(db, id)).Segments.SelectMany(x => x.Pauses)
            .OrderBy(p => p.StartedAt).Select(p => (p.StartedAt, p.EndedAt)).ToList();
        return (id, shiftId, s, before);
    }

    private static void AssertKept(List<(DateTimeOffset, DateTimeOffset?)> before, CiSupport.Approved r, int expectedRows)
    {
        Assert.Equal(expectedRows, r.Pauses.Count);
        Assert.Equal(before, r.Pauses.Select(p => (p.Start, p.End)).ToList());
        Assert.Equal("Completed", r.Status);
    }

    [ScratchFact]
    public async Task Approval_inside_first_outage_ends_the_session_there_and_the_later_outage_does_not_count()
    {
        var (id, shiftId, s, before) = await SeedAsync((1, 2), (3, 3.5));
        var r = await CiSupport.ApproveAsync(id, shiftId, s.AddMinutes(90));
        Assert.Equal(TimeSpan.FromMinutes(90), r.Billable);
        Assert.Equal(90m, r.Amount);
        AssertKept(before, r, 2);
    }

    [ScratchFact]
    public async Task Approval_inside_a_later_outage_keeps_the_earlier_outage_deducted()
    {
        var (id, shiftId, s, before) = await SeedAsync((1, 2), (3, 3.5));
        var r = await CiSupport.ApproveAsync(id, shiftId, s.AddMinutes(192));
        Assert.Equal(TimeSpan.FromMinutes(132), r.Billable);
        Assert.Equal(132m, r.Amount);
        AssertKept(before, r, 2);
    }

    [ScratchFact]
    public async Task Approval_between_outages_keeps_the_earlier_outage_and_ignores_the_later_one()
    {
        var (id, shiftId, s, before) = await SeedAsync((1, 2), (3, 3.5));
        var r = await CiSupport.ApproveAsync(id, shiftId, s.AddMinutes(150));
        Assert.Equal(TimeSpan.FromMinutes(90), r.Billable);
        Assert.Equal(90m, r.Amount);
        AssertKept(before, r, 2);
    }

    [ScratchFact]
    public async Task Approval_after_all_outages_keeps_every_outage_deducted_and_normal_time_between_billable()
    {
        var (id, shiftId, s, before) = await SeedAsync((1, 2), (3, 3.5));
        var r = await CiSupport.ApproveAsync(id, shiftId, s.AddHours(4));
        Assert.Equal(TimeSpan.FromMinutes(150), r.Billable);
        Assert.Equal(150m, r.Amount);
        AssertKept(before, r, 2);
    }

    // The user's example, shifted to hours after the start: outage 1h-2h, normal 2h-3h, outage 3h-5h, approved end at 4h.
    [ScratchFact]
    public async Task Users_example_outage_normal_outage_with_approved_end_inside_the_second_outage()
    {
        var (id, shiftId, s, before) = await SeedAsync((1, 2), (3, 5));
        var r = await CiSupport.ApproveAsync(id, shiftId, s.AddHours(4));
        Assert.Equal(TimeSpan.FromHours(3), r.Billable); // 4h - outage 1 (1h); 3h-4h of outage 2 is session time; nothing after 4h
        Assert.Equal(180m, r.Amount);
        AssertKept(before, r, 2);
    }
}
