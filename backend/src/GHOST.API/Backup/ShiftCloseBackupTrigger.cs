using GHOST.Application.Backup;
using GHOST.Domain.Entities;
using Microsoft.Extensions.Options;

namespace GHOST.API.Backup;

/// <summary>
/// Starts a verified backup after a shift closes. The shift close has already been saved, so a backup
/// problem is logged and never turns a successful close into an error. Does nothing unless Backup:Enabled.
/// </summary>
public sealed class ShiftCloseBackupTrigger
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ScheduledBackupOptions _options;
    private readonly ILogger<ShiftCloseBackupTrigger> _logger;

    public ShiftCloseBackupTrigger(IServiceScopeFactory scopes, IOptions<ScheduledBackupOptions> options, ILogger<ShiftCloseBackupTrigger> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    public bool ShouldRun => _options.Enabled && _options.BackupOnShiftClose && !string.IsNullOrWhiteSpace(_options.Directory);

    public void Fire(Guid actorUserId)
    {
        if (!ShouldRun)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<BackupHandler>();
                var result = await handler.RunAsync(BackupType.ShiftClose, actorUserId);
                if (!result.IsSuccess)
                    _logger.LogError("Shift-close backup failed: {Error}", result.Error);
                else if (result.Value!.ExternalCopy is { } copy && copy != "Copied")
                    _logger.LogError("Shift-close backup external copy problem: {Copy}", copy);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Shift-close backup crashed.");
            }
        });
    }
}
