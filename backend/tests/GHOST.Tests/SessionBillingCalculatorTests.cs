using GHOST.Domain.Services;

namespace GHOST.Tests;

public sealed class SessionBillingCalculatorTests
{
    [Fact]
    public void CalculateTotalAmount_ChargesExactUsage_NotRoundedUpToAnHour()
    {
        var amount = SessionBillingCalculator.CalculateTotalAmount(
        [
            (TimeSpan.FromMinutes(61), 60m)
        ]);

        Assert.Equal(61.00m, amount);
    }

    [Fact]
    public void CalculateTotalAmount_CalculatesPartialHourProportionally()
    {
        var amount = SessionBillingCalculator.CalculateTotalAmount(
        [
            (TimeSpan.FromMinutes(45), 40m)
        ]);

        Assert.Equal(30.00m, amount);
    }

    [Fact]
    public void CalculateTotalAmount_SumsSegmentsUsingEachCapturedHourlyRate()
    {
        var amount = SessionBillingCalculator.CalculateTotalAmount(
        [
            (TimeSpan.FromMinutes(30), 60m),
            (TimeSpan.FromMinutes(30), 40m)
        ]);

        Assert.Equal(50.00m, amount);
    }

    [Fact]
    public void CalculateTotalAmount_RoundsOnlyAfterSummingAllSegments()
    {
        // Each segment is exactly half a piastre before rounding.
        var amount = SessionBillingCalculator.CalculateTotalAmount(
        [
            (TimeSpan.FromSeconds(1), 18m),
            (TimeSpan.FromSeconds(1), 18m)
        ]);

        Assert.Equal(0.01m, amount);
    }

    [Fact]
    public void CalculateTotalAmount_ReturnsZeroForNoSegments()
    {
        var amount = SessionBillingCalculator.CalculateTotalAmount([]);

        Assert.Equal(0.00m, amount);
    }

    [Fact]
    public void CalculateTotalAmount_RejectsNegativeDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SessionBillingCalculator.CalculateTotalAmount(
            [
                (TimeSpan.FromMinutes(-1), 60m)
            ]));
    }

    [Fact]
    public void CalculateTotalAmount_RejectsNonPositiveRate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SessionBillingCalculator.CalculateTotalAmount(
            [
                (TimeSpan.FromMinutes(10), 0m)
            ]));
    }
}
