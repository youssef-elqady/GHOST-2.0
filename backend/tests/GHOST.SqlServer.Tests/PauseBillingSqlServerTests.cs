namespace GHOST.SqlServer.Tests;

/// <summary>Power outage pauses and billable time on a real SQL Server: cases the current code is expected to handle.</summary>
public sealed class PauseBillingSqlServerTests : IAsyncLifetime
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

    [ScratchFact]
    public async Task One_outage_is_deducted_once()
    {
        var (id, s, now) = await NewAsync();
        await CiSupport.MarkOutageAsync(id, s.AddHours(1), s.AddHours(2));
        var r = await CiSupport.ReadAsync(id, now);
        Assert.Equal(1, r.Pauses);
        Assert.Equal(TimeSpan.FromHours(4), r.Billable);
    }

    [ScratchFact]
    public async Task Exact_replay_of_the_same_outage_stores_no_second_pause()
    {
        var (id, s, now) = await NewAsync();
        await CiSupport.MarkOutageAsync(id, s.AddHours(1), s.AddHours(2));
        await CiSupport.MarkOutageAsync(id, s.AddHours(1), s.AddHours(2));
        await CiSupport.MarkOutageAsync(id, s.AddHours(1), s.AddHours(2));
        var r = await CiSupport.ReadAsync(id, now);
        Assert.Equal(1, r.Pauses);
        Assert.Equal(TimeSpan.FromHours(4), r.Billable);
    }

    [ScratchFact]
    public async Task Two_separate_outages_are_both_deducted_and_time_between_them_stays_billable()
    {
        var (id, s, now) = await NewAsync();
        await CiSupport.MarkOutageAsync(id, s.AddHours(1), s.AddHours(2));
        await CiSupport.MarkOutageAsync(id, s.AddHours(3), s.AddMinutes(210));
        var r = await CiSupport.ReadAsync(id, now);
        Assert.Equal(2, r.Pauses);
        Assert.Equal(TimeSpan.FromHours(5) - TimeSpan.FromHours(1) - TimeSpan.FromMinutes(30), r.Billable);
    }
}
