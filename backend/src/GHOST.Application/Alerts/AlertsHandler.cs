using GHOST.Application.Settings;

namespace GHOST.Application.Alerts;

/// <summary>Smart alerts (SRS FR-68), computed on demand from existing data using the admin thresholds.</summary>
public sealed class AlertsHandler
{
    public const int MaxRangeDays = 92;
    public const int DefaultRangeDays = 7;

    private readonly IAlertQueries _queries;
    private readonly SettingsHandler _settings;
    private readonly TimeProvider _time;

    public AlertsHandler(IAlertQueries queries, SettingsHandler settings, TimeProvider time)
    {
        _queries = queries;
        _settings = settings;
        _time = time;
    }

    public async Task<Common.Result.Result<IReadOnlyList<AlertDto>>> GetAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default)
    {
        var end = to ?? _time.GetUtcNow();
        var start = from ?? end.AddDays(-DefaultRangeDays);
        if (start >= end)
            return Common.Result.Result<IReadOnlyList<AlertDto>>.Failure("from must be before to.");
        if (end - start > TimeSpan.FromDays(MaxRangeDays))
            return Common.Result.Result<IReadOnlyList<AlertDto>>.Failure($"The range cannot exceed {MaxRangeDays} days.");

        var values = await _settings.GetValuesAsync(cancellationToken);
        var thresholds = new AlertThresholds(
            values[SettingsCatalog.WithdrawalAlertThreshold],
            values[SettingsCatalog.CashShortageAlertThreshold],
            values[SettingsCatalog.DiscountAlertPercent],
            (int)values[SettingsCatalog.CancellationsAlertCount],
            values[SettingsCatalog.StocktakeLossAlertValue]);

        var alerts = await _queries.GetAlertsAsync(start, end, thresholds, cancellationToken);
        return Common.Result.Result<IReadOnlyList<AlertDto>>.Success(
            alerts.OrderByDescending(a => a.OccurredAt).ToList());
    }
}
