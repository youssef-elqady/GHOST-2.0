using GHOST.Domain.Entities;
using GHOST.Domain.Enums;

namespace GHOST.Tests;

/// <summary>C1: overlapping pauses are deducted once (union), clipped to the segment and to the calculation time.</summary>
public sealed class PauseUnionBillingTests
{
    // Rate 60 EGP/h, so 1 minute = 1 EGP.
    private static Session NewSession() => new(Guid.NewGuid(), Guid.NewGuid(), PlayType.Single, 60m);

    private static decimal Amount(Session s, int minutes) => s.GetTotalAmount(s.StartedAt.AddMinutes(minutes));

    [Fact]
    public void Identical_pauses_are_deducted_once()
    {
        var s = NewSession(); var t = s.StartedAt;
        s.MarkPowerOutage(t.AddMinutes(60), t.AddMinutes(120));
        s.MarkPowerOutage(t.AddMinutes(60), t.AddMinutes(120));
        Assert.Equal(240m, Amount(s, 300));
    }

    [Fact]
    public void Same_start_different_end_deducts_the_longer_once()
    {
        var s = NewSession(); var t = s.StartedAt;
        s.MarkPowerOutage(t.AddMinutes(60), t.AddMinutes(120));
        s.MarkPowerOutage(t.AddMinutes(60), t.AddMinutes(150));
        Assert.Equal(210m, Amount(s, 300));
    }

    [Fact]
    public void Partial_overlap_deducts_the_union()
    {
        var s = NewSession(); var t = s.StartedAt;
        s.MarkPowerOutage(t.AddMinutes(60), t.AddMinutes(120));
        s.MarkPowerOutage(t.AddMinutes(90), t.AddMinutes(150));
        Assert.Equal(210m, Amount(s, 300));
    }

    [Fact]
    public void One_pause_inside_another_deducts_the_larger_once()
    {
        var s = NewSession(); var t = s.StartedAt;
        s.MarkPowerOutage(t.AddMinutes(60), t.AddMinutes(180));
        s.MarkPowerOutage(t.AddMinutes(90), t.AddMinutes(150));
        Assert.Equal(180m, Amount(s, 300));
    }

    [Fact]
    public void Separate_pauses_are_both_deducted()
    {
        var s = NewSession(); var t = s.StartedAt;
        s.MarkPowerOutage(t.AddMinutes(60), t.AddMinutes(90));
        s.MarkPowerOutage(t.AddMinutes(120), t.AddMinutes(150));
        Assert.Equal(240m, Amount(s, 300));
    }

    [Fact]
    public void Adjacent_pauses_deduct_the_sum()
    {
        var s = NewSession(); var t = s.StartedAt;
        s.MarkPowerOutage(t.AddMinutes(60), t.AddMinutes(90));
        s.MarkPowerOutage(t.AddMinutes(90), t.AddMinutes(120));
        Assert.Equal(240m, Amount(s, 300));
    }

    [Fact]
    public void Active_pause_counts_up_to_the_calculation_time()
    {
        var s = NewSession(); var t = s.StartedAt;
        s.PauseActiveDevice();
        // Pause started at about "now"; calculate 100 minutes later: nearly all time is paused.
        var duration = s.GetTotalBillableDuration(t.AddMinutes(100));
        Assert.True(duration < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Calculation_before_a_pause_starts_deducts_nothing()
    {
        var s = NewSession(); var t = s.StartedAt;
        s.MarkPowerOutage(t.AddMinutes(60), t.AddMinutes(120));
        Assert.Equal(30m, Amount(s, 30));
    }

    [Fact]
    public void Calculation_inside_a_pause_deducts_only_up_to_that_time()
    {
        var s = NewSession(); var t = s.StartedAt;
        s.MarkPowerOutage(t.AddMinutes(60), t.AddMinutes(120));
        Assert.Equal(60m, Amount(s, 90));
    }

    [Fact]
    public void Pause_starting_before_the_segment_is_clipped_to_the_segment_start()
    {
        var s = NewSession(); var t = s.StartedAt;
        s.MarkPowerOutage(t.AddMinutes(-30), t.AddMinutes(20));
        Assert.Equal(40m, Amount(s, 60));
    }

    [Fact]
    public void Billable_is_never_negative()
    {
        var s = NewSession(); var t = s.StartedAt;
        s.MarkPowerOutage(t.AddMinutes(-30), t.AddMinutes(500));
        Assert.Equal(0m, Amount(s, 60));
    }

    [Fact]
    public void Pause_rows_are_not_changed_by_the_calculation()
    {
        var s = NewSession(); var t = s.StartedAt;
        s.MarkPowerOutage(t.AddMinutes(60), t.AddMinutes(120));
        s.MarkPowerOutage(t.AddMinutes(90), t.AddMinutes(150));
        var before = s.Segments.SelectMany(x => x.Pauses).Select(p => (p.StartedAt, p.EndedAt)).ToList();
        _ = Amount(s, 300);
        var after = s.Segments.SelectMany(x => x.Pauses).Select(p => (p.StartedAt, p.EndedAt)).ToList();
        Assert.Equal(before, after);
    }
}
