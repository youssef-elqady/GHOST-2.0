using GHOST.Application.Sessions;

namespace GHOST.API.Outage;

public sealed class OutageOptions
{
    public const string SectionName = "Outage";

    /// <summary>A start-up gap longer than this (seconds) counts as a power cut or crash. A normal restart is shorter.</summary>
    public int GapThresholdSeconds { get; set; } = 120;

    public int HeartbeatSeconds { get; set; } = 30;
}

/// <summary>
/// At start-up flags sessions that were running during a power cut, then writes a heartbeat every few seconds.
/// Errors are logged and retried on the next beat; they never stop the host.
/// </summary>
public sealed class OutageRecoveryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly OutageOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<OutageRecoveryService> _logger;

    public OutageRecoveryService(
        IServiceScopeFactory scopes,
        Microsoft.Extensions.Options.IOptions<OutageOptions> options,
        TimeProvider time,
        ILogger<OutageRecoveryService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var threshold = TimeSpan.FromSeconds(Math.Max(30, _options.GapThresholdSeconds));
        var period = TimeSpan.FromSeconds(Math.Clamp(_options.HeartbeatSeconds, 5, 60));

        var recovered = false;
        using var timer = new PeriodicTimer(period);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<PowerOutageHandler>();
                var now = _time.GetUtcNow();
                if (!recovered)
                {
                    var result = await handler.RecoverAsync(now, threshold, stoppingToken);
                    recovered = true;
                    if (result.OutageDetected)
                        _logger.LogWarning(
                            "Power outage detected: system was down for {Gap}. {Count} session(s) need Admin review.",
                            result.Gap, result.SessionsFlagged);
                }
                else
                {
                    await handler.BeatAsync(now, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Outage heartbeat/recovery failed; will retry on the next beat.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try { return await timer.WaitForNextTickAsync(token); }
        catch (OperationCanceledException) { return false; }
    }
}
