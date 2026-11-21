using GHOST.Application.Backup;

namespace GHOST.Infrastructure.Persistence;

/// <summary>
/// Refuses to copy backups unless the destination is the expected physical drive (marker file with the expected id),
/// is present, and accepts a write. The drive root is never created, so an unplugged drive whose letter is free,
/// or a different drive that took the letter, cannot receive backups by accident.
/// </summary>
public sealed class ExternalDriveGuard : IExternalDriveGuard
{
    public string? Check(string externalDirectory, string? markerPath, string? expectedDriveId)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(externalDirectory));
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return $"External drive {root} is not connected.";

            if (!string.IsNullOrWhiteSpace(expectedDriveId))
            {
                if (string.IsNullOrWhiteSpace(markerPath))
                    return "Backup:ExternalDriveId is set but Backup:ExternalDriveMarker is not.";
                if (!File.Exists(markerPath))
                    return "The external drive identity file was not found. The drive is missing or is a different drive.";
                if (!string.Equals(File.ReadAllText(markerPath).Trim(), expectedDriveId.Trim(), StringComparison.Ordinal))
                    return "A different drive is connected at this location (identity does not match).";
            }

            Directory.CreateDirectory(externalDirectory);
            var probe = Path.Combine(externalDirectory, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "The external drive is not writable: " + exception.Message;
        }
    }
}
