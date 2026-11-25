namespace GHOST.SqlServer.Tests;

/// <summary>Admin approval after outages, the case the current code is expected to handle (end after every outage).</summary>
public sealed class OutageApprovalSqlServerTests : IAsyncLifetime
{
    public Task InitializeAsync() => ScratchDb.EnterAsync();
    public Task DisposeAsync() => ScratchDb.LeaveAsync();

    [ScratchFact]
    public async Task Approval_after_all_outages_keeps_every_outage_deducted_and_normal_time_between_billable()
    {
        var (_, _, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var (id, s) = await CiSupport.NewSessionStartedAsync(shiftId, TimeSpan.FromHours(5));
        await CiSupport.MarkOutageAsync(id, s.AddHours(1), s.AddHours(2));
        await CiSupport.MarkOutageAsync(id, s.AddHours(3), s.AddMinutes(210));
        var r = await CiSupport.ApproveAsync(id, shiftId, s.AddHours(4));
        // 4h of play minus 1h and 30m of outages, pause rows kept for the audit trail.
        Assert.Equal(TimeSpan.FromMinutes(150), r.Billable);
        Assert.Equal(2, r.Pauses);
    }
}
