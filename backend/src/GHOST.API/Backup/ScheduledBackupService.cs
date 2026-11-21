using GHOST.Application.Backup;
using GHOST.Domain.Entities;

namespace GHOST.API.Backup;

public sealed class ScheduledBackupOptions
{
    public const string SectionName = "Backup";

    public bool Enabled { get; set; }

    /// <summary>Folder SQL Server writes to. Required when backups are enabled.</summary>
    public string? Directory { get; set; }

    public int RetentionCount { get; set; } = 7;

    /// <summary>Second location (for example an external drive). Every verified backup is copied here and the newest RetentionCount are kept.</summary>
    public string? ExternalDirectory { get; set; }

    /// <summary>Also back up after each shift close.</summary>
    public bool BackupOnShiftClose { get; set; } = true;

    /// <summary>Daily run time, local server time, HH:mm.</summary>
    public string DailyAt { get; set; } = "03:00";
}

/// <summary>Runs one scheduled backup a day. Failures are recorded and logged, never thrown into the host.</summary>
public sealed class ScheduledBackupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ScheduledBackupOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ScheduledBackupService> _logger;

    public ScheduledBackupService(
        IServiceScopeFactory scopes,
        Microsoft.Extensions.Options.IOptions<ScheduledBackupOptions> options,
        TimeProvider time,
        ILogger<ScheduledBackupService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(_options.Directory) || !TimeOnly.TryParse(_options.DailyAt, out var at))
        {
            _logger.LogError("Backup is enabled but Backup:Directory or Backup:DailyAt is invalid. Scheduler not started.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NextRunDelay(_time.GetLocalNow(), at);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using var scope = _scopes.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<BackupHandler>();
                var result = await handler.RunAsync(BackupType.Scheduled, null, stoppingToken);
                if (!result.IsSuccess)
                    _logger.LogError("Scheduled backup failed: {Error}", result.Error);
                else if (result.Value!.ExternalCopy is { } copy && copy != "Copied")
                    _logger.LogError("Scheduled backup external copy problem: {Copy}", copy);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Scheduled backup crashed.");
            }
        }
    }

    public static TimeSpan NextRunDelay(DateTimeOffset localNow, TimeOnly at)
    {
        var next = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, at.Hour, at.Minute, 0, localNow.Offset);
        if (next <= localNow)
            next = next.AddDays(1);
        return next - localNow;
    }
}
