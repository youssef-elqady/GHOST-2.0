namespace GHOST.Domain.Services;

public static class SessionBillingCalculator
{
    /// <summary>
    /// Calculates the total session charge from each segment's billable duration
    /// and the hourly rate captured when that segment began.
    /// The unrounded segment amounts are summed first; only the final total is
    /// rounded to two decimal places.
    /// </summary>
    public static decimal CalculateTotalAmount(
        IEnumerable<(TimeSpan BillableDuration, decimal AppliedHourlyRate)> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        decimal unroundedTotal = 0m;

        foreach (var (billableDuration, appliedHourlyRate) in segments)
        {
            if (billableDuration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(
                    nameof(segments),
                    "Billable duration cannot be negative.");

            if (appliedHourlyRate <= 0m)
                throw new ArgumentOutOfRangeException(
                    nameof(segments),
                    "Applied hourly rate must be greater than zero.");

            var segmentAmount =
                billableDuration.Ticks
                * appliedHourlyRate
                / TimeSpan.TicksPerHour;

            unroundedTotal += segmentAmount;
        }

        return decimal.Round(unroundedTotal, 2, MidpointRounding.AwayFromZero);
    }
}
