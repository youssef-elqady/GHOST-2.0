using System.Text.Json;
using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Result;
using GHOST.Domain.Entities;

namespace GHOST.Application.Backup;

public sealed record BackupOptions(
    string Directory,
    int RetentionCount,
    string? ExternalDirectory = null,
    string? ExternalDriveMarker = null,
    string? ExternalDriveId = null);

/// <summary>
/// Checks that the external destination is the right, present and writable drive before anything is copied.
/// Returns null when the drive is acceptable, otherwise the reason. Never creates the drive's root.
/// </summary>
public interface IExternalDriveGuard
{
    string? Check(string externalDirectory, string? markerPath, string? expectedDriveId);
}

public sealed record BackupFileResult(long? SizeBytes);

/// <summary>Talks to the database server and the file system. Implemented in Infrastructure.</summary>
public interface IDatabaseBackupGateway
{
    /// <summary>Writes a checksummed backup to <paramref name="filePath"/>.</summary>
    Task<BackupFileResult> BackupAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>RESTORE VERIFYONLY with CHECKSUM. Throws when the file is unreadable or corrupt.</summary>
    Task VerifyAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a verified backup to a second location (for example an external drive) and keeps the newest
    /// <paramref name="keep"/> GHOST_*.bak files there. Returns null on success or the error text.
    /// </summary>
    Task<string?> CopyToExternalAsync(string filePath, string externalDirectory, int keep, CancellationToken cancellationToken = default);

    /// <summary>Best effort. Returns false when the file could not be removed.</summary>
    bool TryDelete(string filePath);
}

public sealed record BackupDto(
    Guid Id,
    string Type,
    string Status,
    string? FilePath,
    long? SizeBytes,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? PrunedAt,
    string? ExternalCopy = null);

/// <summary>
/// Backup with verification and retention (SRS FR-81). A backup only counts as succeeded after
/// RESTORE VERIFYONLY passes. Only one backup runs at a time.
/// </summary>
public sealed class BackupHandler
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IBackupRepository _records;
    private readonly IDatabaseBackupGateway _gateway;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _audit;
    private readonly TimeProvider _time;
    private readonly BackupOptions _options;
    private readonly IExternalDriveGuard? _driveGuard;
    private readonly ISystemAuditTrail? _systemAudit;

    public BackupHandler(
        IBackupRepository records,
        IDatabaseBackupGateway gateway,
        IUnitOfWork unitOfWork,
        IAuditTrail audit,
        TimeProvider time,
        BackupOptions options,
        IExternalDriveGuard? driveGuard = null,
        ISystemAuditTrail? systemAudit = null)
    {
        _driveGuard = driveGuard;
        _systemAudit = systemAudit;
        _records = records;
        _gateway = gateway;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _time = time;
        _options = options;
    }

    public async Task<IReadOnlyList<BackupDto>> ListAsync(int take, CancellationToken cancellationToken = default)
        => (await _records.ListAsync(Math.Clamp(take, 1, 100), cancellationToken)).Select(ToDto).ToList();

    public async Task<Result<BackupDto>> RunAsync(
        BackupType type, Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Directory))
            return Result<BackupDto>.Failure("Backup:Directory is not configured.");

        if (!await Gate.WaitAsync(0, cancellationToken))
            return Result<BackupDto>.Failure("A backup is already running.");

        try
        {
            var now = _time.GetUtcNow();
            var fileName = $"GHOST_{now:yyyyMMdd_HHmmss}_{type}.bak";
            var filePath = Path.Combine(_options.Directory, fileName);

            BackupRecord record;
            try
            {
                var written = await _gateway.BackupAsync(filePath, cancellationToken);
                await _gateway.VerifyAsync(filePath, cancellationToken);
                record = BackupRecord.Succeeded(type, filePath, written.SizeBytes, actorUserId, now, _time.GetUtcNow());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A failed or unverifiable file is never kept as a usable backup.
                _gateway.TryDelete(filePath);
                record = BackupRecord.Failed(type, filePath, exception.Message, actorUserId, now);
            }

            await _records.AddAsync(record, cancellationToken);
            if (actorUserId is null && record.Status == BackupStatus.Failed)
                _systemAudit?.Record("Backup.Failed", "BackupRecord", record.Id.ToString(),
                    JsonSerializer.Serialize(new { type = type.ToString(), record.Error }));
            if (actorUserId is { } actor)
                _audit.Record(actor, record.Status == BackupStatus.Succeeded ? "Backup.Created" : "Backup.Failed",
                    "BackupRecord", record.Id.ToString(),
                    JsonSerializer.Serialize(new { type = type.ToString(), record.FilePath }));

            string? externalCopy = null;
            if (record.Status == BackupStatus.Succeeded)
            {
                await PruneAsync(record, cancellationToken);
                if (!string.IsNullOrWhiteSpace(_options.ExternalDirectory) && record.FilePath is not null)
                {
                    // Identity, presence and writability first. Nothing is copied to a wrong or missing drive,
                    // and a copy only counts after the size check inside CopyToExternalAsync.
                    var failure = _driveGuard?.Check(
                        _options.ExternalDirectory, _options.ExternalDriveMarker, _options.ExternalDriveId);
                    failure ??= await _gateway.CopyToExternalAsync(
                        record.FilePath, _options.ExternalDirectory, Math.Max(_options.RetentionCount, 1), cancellationToken);
                    externalCopy = failure is null ? "Copied" : "Failed: " + failure;
                    if (failure is null)
                        record.MarkExternalCopied(_time.GetUtcNow());
                    else
                    {
                        record.MarkExternalCopyFailed(failure);
                        var details = JsonSerializer.Serialize(new { _options.ExternalDirectory, failure });
                        if (actorUserId is { } who)
                            _audit.Record(who, "Backup.ExternalCopyFailed", "BackupRecord", record.Id.ToString(), details);
                        else
                            _systemAudit?.Record("Backup.ExternalCopyFailed", "BackupRecord", record.Id.ToString(), details);
                    }
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return record.Status == BackupStatus.Succeeded
                ? Result<BackupDto>.Success(ToDto(record) with { ExternalCopy = externalCopy })
                : Result<BackupDto>.Failure("Backup failed: " + record.Error);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task PruneAsync(BackupRecord justCreated, CancellationToken cancellationToken)
    {
        var live = (await _records.ListLiveAsync(cancellationToken))
            .Where(r => r.Id != justCreated.Id)
            .OrderByDescending(r => r.CreatedAt)
            .ToList();

        // Keep the newest N including the one just made.
        foreach (var old in live.Skip(Math.Max(0, _options.RetentionCount - 1)))
        {
            if (old.FilePath is null || _gateway.TryDelete(old.FilePath))
                old.MarkPruned(_time.GetUtcNow());
        }
    }

    private static BackupDto ToDto(BackupRecord r) => new(
        r.Id, r.Type.ToString(), r.Status.ToString(), r.FilePath, r.SizeBytes, r.Error,
        r.CreatedAt, r.VerifiedAt, r.PrunedAt);
}
