namespace GHOST.Domain.Entities;

public enum BackupType
{
    Manual = 1,
    Scheduled = 2,
    ShiftClose = 3
}

public enum BackupStatus
{
    Succeeded = 1,
    Failed = 2
}

/// <summary>Result of one backup attempt (SRS FR-81). A succeeded backup is also restore-verified.</summary>
public sealed class BackupRecord
{
    public const int MaxPathLength = 500;
    public const int MaxErrorLength = 1000;

    public Guid Id { get; private set; }

    public BackupType Type { get; private set; }

    public BackupStatus Status { get; private set; }

    public string? FilePath { get; private set; }

    public long? SizeBytes { get; private set; }

    public string? Error { get; private set; }

    public Guid? RequestedById { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set when RESTORE VERIFYONLY succeeded on the file.</summary>
    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>Set when the retention policy removed the file.</summary>
    public DateTimeOffset? PrunedAt { get; private set; }

    private BackupRecord()
    {
    }

    public static BackupRecord Succeeded(
        BackupType type, string filePath, long? sizeBytes, Guid? requestedById, DateTimeOffset createdAt, DateTimeOffset verifiedAt)
        => new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            Status = BackupStatus.Succeeded,
            FilePath = filePath,
            SizeBytes = sizeBytes,
            RequestedById = requestedById,
            CreatedAt = createdAt,
            VerifiedAt = verifiedAt
        };

    public static BackupRecord Failed(
        BackupType type, string? filePath, string error, Guid? requestedById, DateTimeOffset createdAt)
        => new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            Status = BackupStatus.Failed,
            FilePath = filePath,
            Error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error,
            RequestedById = requestedById,
            CreatedAt = createdAt
        };

    public void MarkPruned(DateTimeOffset now) => PrunedAt = now;
}
