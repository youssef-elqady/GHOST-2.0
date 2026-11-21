using GHOST.API.Backup;
using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Alerts;
using GHOST.Application.Backup;
using GHOST.Application.Settings;
using GHOST.Domain.Entities;

namespace GHOST.Tests.Operations;

public sealed class SettingsBackupAlertsTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    private sealed class FakeUow : IUnitOfWork
    {
        public int Saves;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(0);
        }
    }

    private sealed class FakeAudit : IAuditTrail
    {
        public List<string> Actions = new();

        public void Record(Guid actorUserId, string action, string entityType, string entityId, string? newValuesJson = null)
            => Actions.Add(action);
    }

    private sealed class FakeSettings : ISettingsRepository
    {
        public List<SystemSetting> Rows = new();

        public Task<IReadOnlyList<SystemSetting>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SystemSetting>>(Rows.ToList());

        public Task<SystemSetting?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(Rows.FirstOrDefault(r => r.Key == key));

        public Task AddAsync(SystemSetting setting, CancellationToken cancellationToken = default)
        {
            Rows.Add(setting);
            return Task.CompletedTask;
        }
    }

    private static SettingsHandler Settings(FakeSettings repo, FakeUow? uow = null, FakeAudit? audit = null)
        => new(repo, uow ?? new FakeUow(), audit ?? new FakeAudit(), TimeProvider.System);

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("12.5")]
    [InlineData("0")]
    [InlineData("101")]
    public async Task Update_RejectsBadValues(string value)
    {
        var uow = new FakeUow();
        var result = await Settings(new FakeSettings(), uow).UpdateAsync(Admin, SettingsCatalog.DiscountAlertPercent, value);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, uow.Saves);
    }

    [Fact]
    public async Task Update_RejectsUnknownKey()
    {
        var result = await Settings(new FakeSettings()).UpdateAsync(Admin, "Typo:Key", "5");
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Update_SavesOnceAndAudits_AndUpdatesExistingRow()
    {
        var repo = new FakeSettings();
        var uow = new FakeUow();
        var audit = new FakeAudit();
        var handler = Settings(repo, uow, audit);

        await handler.UpdateAsync(Admin, SettingsCatalog.DiscountAlertPercent, "30");
        await handler.UpdateAsync(Admin, SettingsCatalog.DiscountAlertPercent, " 40 ");

        Assert.Single(repo.Rows);
        Assert.Equal("40", repo.Rows[0].Value);
        Assert.Equal(2, uow.Saves);
        Assert.Equal(new[] { "Setting.Changed", "Setting.Changed" }, audit.Actions);
    }

    [Fact]
    public async Task Values_FallBackToDefaults_AndIgnoreCorruptRows()
    {
        var repo = new FakeSettings();
        repo.Rows.Add(new SystemSetting(SettingsCatalog.DiscountAlertPercent, "garbage", Admin, DateTimeOffset.UtcNow));
        repo.Rows.Add(new SystemSetting(SettingsCatalog.WithdrawalAlertThreshold, "900", Admin, DateTimeOffset.UtcNow));

        var values = await Settings(repo).GetValuesAsync();

        Assert.Equal(20m, values[SettingsCatalog.DiscountAlertPercent]);
        Assert.Equal(900m, values[SettingsCatalog.WithdrawalAlertThreshold]);
        Assert.Equal(SettingsCatalog.All.Count, values.Count);
    }

    private sealed class FakeAlertQueries : IAlertQueries
    {
        public AlertThresholds? Seen;

        public Task<IReadOnlyList<AlertDto>> GetAlertsAsync(
            DateTimeOffset from, DateTimeOffset to, AlertThresholds thresholds, CancellationToken cancellationToken = default)
        {
            Seen = thresholds;
            return Task.FromResult<IReadOnlyList<AlertDto>>(new List<AlertDto>
            {
                new("A", "Warning", from, "Shift", Guid.NewGuid(), null, null, 1, 1, null),
                new("B", "Warning", to.AddMinutes(-1), "Shift", Guid.NewGuid(), null, null, 1, 1, null)
            });
        }
    }

    [Fact]
    public async Task Alerts_UseAdminThresholds_AndSortNewestFirst()
    {
        var repo = new FakeSettings();
        repo.Rows.Add(new SystemSetting(SettingsCatalog.CancellationsAlertCount, "5", Admin, DateTimeOffset.UtcNow));
        var queries = new FakeAlertQueries();
        var handler = new AlertsHandler(queries, Settings(repo), TimeProvider.System);

        var result = await handler.GetAsync(null, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, queries.Seen!.CancellationsPerShift);
        Assert.Equal("B", result.Value![0].Type);
    }

    [Fact]
    public async Task Alerts_RejectBackwardsAndHugeRanges()
    {
        var handler = new AlertsHandler(new FakeAlertQueries(), Settings(new FakeSettings()), TimeProvider.System);
        var now = DateTimeOffset.UtcNow;

        Assert.False((await handler.GetAsync(now, now.AddDays(-1))).IsSuccess);
        Assert.False((await handler.GetAsync(now.AddDays(-200), now)).IsSuccess);
    }

    private sealed class FakeBackups : IBackupRepository
    {
        public List<BackupRecord> Rows = new();

        public Task AddAsync(BackupRecord record, CancellationToken cancellationToken = default)
        {
            Rows.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BackupRecord>> ListAsync(int take, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BackupRecord>>(Rows.OrderByDescending(r => r.CreatedAt).Take(take).ToList());

        public Task<IReadOnlyList<BackupRecord>> ListLiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BackupRecord>>(Rows
                .Where(r => r.Status == BackupStatus.Succeeded && r.PrunedAt == null).ToList());
    }

    private sealed class FakeGateway : IDatabaseBackupGateway
    {
        public bool FailVerify;
        public bool FailBackup;
        public List<string> Deleted = new();

        public Task<BackupFileResult> BackupAsync(string filePath, CancellationToken cancellationToken = default)
            => FailBackup ? throw new InvalidOperationException("disk full") : Task.FromResult(new BackupFileResult(123));

        public Task VerifyAsync(string filePath, CancellationToken cancellationToken = default)
            => FailVerify ? throw new InvalidOperationException("checksum mismatch") : Task.CompletedTask;

        public string? ExternalFailure;
        public List<string> Copied = new();

        public Task<string?> CopyToExternalAsync(string filePath, string externalDirectory, int keep, CancellationToken cancellationToken = default)
        {
            Copied.Add(filePath + " -> " + externalDirectory + " keep " + keep);
            return Task.FromResult(ExternalFailure);
        }

        public bool TryDelete(string filePath)
        {
            Deleted.Add(filePath);
            return true;
        }
    }

    private static BackupHandler Backup(FakeBackups repo, FakeGateway gateway, int retention = 7, string dir = "/bk")
        => new(repo, gateway, new FakeUow(), new FakeAudit(), TimeProvider.System, new BackupOptions(dir, retention));

    private static BackupHandler BackupWithExternal(FakeBackups repo, FakeGateway gateway)
        => new(repo, gateway, new FakeUow(), new FakeAudit(), TimeProvider.System, new BackupOptions("/bk", 7, "/usb"));

    [Fact]
    public async Task Backup_CopiesVerifiedFileToExternalDrive_AndReportsIt()
    {
        var gateway = new FakeGateway();
        var result = await BackupWithExternal(new FakeBackups(), gateway).RunAsync(BackupType.ShiftClose, Admin);

        Assert.True(result.IsSuccess);
        Assert.Equal("Copied", result.Value!.ExternalCopy);
        Assert.Contains("/usb keep 7", Assert.Single(gateway.Copied));
    }

    [Fact]
    public async Task ExternalCopyFailure_KeepsTheLocalBackup_AndIsReported()
    {
        var repo = new FakeBackups();
        var result = await BackupWithExternal(repo, new FakeGateway { ExternalFailure = "drive not found" }).RunAsync(BackupType.Scheduled, null);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("Failed: drive not found", result.Value!.ExternalCopy);
        Assert.Equal(BackupStatus.Succeeded, repo.Rows.Single().Status);
    }

    private sealed class RejectingGuard : IExternalDriveGuard
    {
        public string? Check(string externalDirectory, string? markerPath, string? expectedDriveId) => "wrong drive";
    }

    [Fact]
    public async Task WrongExternalDrive_IsNeverCopiedTo_AndTheFailureIsRecorded()
    {
        var repo = new FakeBackups();
        var gateway = new FakeGateway();
        var handler = new BackupHandler(repo, gateway, new FakeUow(), new FakeAudit(), TimeProvider.System,
            new BackupOptions("/bk", 7, "/usb", "/usb/.marker", "ID1"), new RejectingGuard());

        var result = await handler.RunAsync(BackupType.Scheduled, null);

        Assert.True(result.IsSuccess);
        Assert.Empty(gateway.Copied);
        Assert.Equal("Failed: wrong drive", result.Value!.ExternalCopy);
        Assert.Equal("wrong drive", repo.Rows.Single().ExternalCopyError);
        Assert.Null(repo.Rows.Single().ExternalCopiedAt);
    }

    [Fact]
    public async Task FailedBackup_IsNeverCopiedExternally()
    {
        var gateway = new FakeGateway { FailVerify = true };
        await BackupWithExternal(new FakeBackups(), gateway).RunAsync(BackupType.Manual, Admin);
        Assert.Empty(gateway.Copied);
    }

    [Fact]
    public async Task Backup_Succeeds_OnlyAfterVerification()
    {
        var repo = new FakeBackups();
        var result = await Backup(repo, new FakeGateway()).RunAsync(BackupType.Manual, Admin);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repo.Rows.Single().VerifiedAt);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Backup_FailureIsRecorded_AndFileRemoved(bool failVerify, bool failBackup)
    {
        var repo = new FakeBackups();
        var gateway = new FakeGateway { FailVerify = failVerify, FailBackup = failBackup };

        var result = await Backup(repo, gateway).RunAsync(BackupType.Scheduled, null);

        Assert.False(result.IsSuccess);
        Assert.Equal(BackupStatus.Failed, repo.Rows.Single().Status);
        Assert.Single(gateway.Deleted);
    }

    [Fact]
    public async Task Backup_KeepsOnlyNewestN()
    {
        var repo = new FakeBackups();
        var gateway = new FakeGateway();
        for (var i = 0; i < 5; i++)
        {
            repo.Rows.Add(BackupRecord.Succeeded(BackupType.Scheduled, $"/bk/{i}.bak", 1, null,
                DateTimeOffset.UtcNow.AddDays(-10 + i), DateTimeOffset.UtcNow));
        }

        await Backup(repo, gateway, retention: 3).RunAsync(BackupType.Manual, Admin);

        Assert.Equal(3, repo.Rows.Count(r => r.PrunedAt == null && r.Status == BackupStatus.Succeeded));
        Assert.Equal(new[] { "/bk/0.bak", "/bk/1.bak", "/bk/2.bak" }, gateway.Deleted.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Backup_RefusesWithoutDirectory()
    {
        var result = await Backup(new FakeBackups(), new FakeGateway(), dir: " ").RunAsync(BackupType.Manual, Admin);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Scheduler_ComputesNextRun()
    {
        var now = new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal(TimeSpan.FromHours(17), ScheduledBackupService.NextRunDelay(now, new TimeOnly(3, 0)));
        Assert.Equal(TimeSpan.FromHours(2), ScheduledBackupService.NextRunDelay(now, new TimeOnly(12, 0)));
    }
}
