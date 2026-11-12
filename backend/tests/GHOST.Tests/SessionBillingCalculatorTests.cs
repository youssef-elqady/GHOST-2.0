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

        Assert.Equal(61m, amount);
    }

    [Fact]
    public void CalculateTotalAmount_CalculatesPartialHourAndCeilsFinalAmount()
    {
        var amount = SessionBillingCalculator.CalculateTotalAmount(
        [
            (TimeSpan.FromMinutes(45), 40m)
        ]);

        Assert.Equal(30m, amount);
    }

    [Fact]
    public void CalculateTotalAmount_SumsSegmentsUsingEachCapturedHourlyRate()
    {
        var amount = SessionBillingCalculator.CalculateTotalAmount(
        [
            (TimeSpan.FromMinutes(30), 60m),
            (TimeSpan.FromMinutes(30), 40m)
        ]);

        Assert.Equal(50m, amount);
    }

    [Fact]
    public void CalculateTotalAmount_CeilingsFractionalSessionTotalToWholeEgp()
    {
        var amount = SessionBillingCalculator.CalculateTotalAmount(
        [
            (TimeSpan.FromMinutes(1), 61m)
        ]);

        Assert.Equal(2m, amount);
    }

    [Fact]
    public void CalculateTotalAmount_CeilingsOnlyAfterSummingAllSegments()
    {
        // Each segment costs 0.15 EGP; the session total is 0.30 EGP.
        // Rounding each segment separately would incorrectly charge 2 EGP.
        var amount = SessionBillingCalculator.CalculateTotalAmount(
        [
            (TimeSpan.FromSeconds(30), 18m),
            (TimeSpan.FromSeconds(30), 18m)
        ]);

        Assert.Equal(1m, amount);
    }

    [Fact]
    public void CalculateTotalAmount_CeilingsTinyPositiveTotalToOneEgp()
    {
        var amount = SessionBillingCalculator.CalculateTotalAmount(
        [
            (TimeSpan.FromSeconds(1), 18m),
            (TimeSpan.FromSeconds(1), 18m)
        ]);

        Assert.Equal(1m, amount);
    }

    [Fact]
    public void CalculateTotalAmount_DoesNotIncreaseAnExactWholeEgpAmount()
    {
        var amount = SessionBillingCalculator.CalculateTotalAmount(
        [
            (TimeSpan.FromMinutes(1), 60m)
        ]);

        Assert.Equal(1m, amount);
    }

    [Fact]
    public void CalculateTotalAmount_ReturnsZeroForNoSegments()
    {
        var amount = SessionBillingCalculator.CalculateTotalAmount([]);

        Assert.Equal(0m, amount);
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
