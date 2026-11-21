using GHOST.Application.Backup;

namespace GHOST.Infrastructure.Persistence;

/// <summary>
/// Refuses to copy backups unless the destination is the expected physical drive (marker file with the expected id),
/// is present, and accepts a write. The drive root is never created, so an unplugged drive whose letter is free,
/// or a different drive that took the letter, cannot receive backups by accident.
/// </summary>
public sealed class ExternalDriveGuard : IExternalDriveGuard
{
    private readonly Func<string, string?> _volumeSerial;
    private readonly Func<IEnumerable<string>> _roots;

    public ExternalDriveGuard() : this(null, null) { }

    /// <summary>Test seams: the volume-serial reader (root path to serial) and the list of drive roots.</summary>
    public ExternalDriveGuard(Func<string, string?>? volumeSerial, Func<IEnumerable<string>>? roots)
    {
        _volumeSerial = volumeSerial ?? ReadVolumeSerial;
        _roots = roots ?? (() => DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName));
    }

    /// <summary>
    /// The expected identity is "MarkerId" (marker only, legacy) or "MarkerId@VolumeSerial" (both must match).
    /// </summary>
    public static (string Id, string? Serial) ParseIdentity(string expected)
    {
        var at = expected.Trim().IndexOf('@');
        return at < 0 ? (expected.Trim(), null) : (expected[..at].Trim(), expected[(at + 1)..].Trim());
    }

    private bool IdentityMatches(string root, string markerFullPath, string expectedId)
    {
        var (id, serial) = ParseIdentity(expectedId);
        try
        {
            if (!File.Exists(markerFullPath)) return false;
            if (!string.Equals(File.ReadAllText(markerFullPath).Trim(), id, StringComparison.Ordinal)) return false;
            if (serial is null) return true;
            var actual = _volumeSerial(root);
            return actual is not null && string.Equals(actual, serial, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public (string Directory, string? Marker) Relocate(string externalDirectory, string? markerPath, string? expectedDriveId)
    {
        if (string.IsNullOrWhiteSpace(expectedDriveId) || string.IsNullOrWhiteSpace(markerPath))
            return (externalDirectory, markerPath);
        try
        {
            var dirFull = Path.GetFullPath(externalDirectory);
            var markerFull = Path.GetFullPath(markerPath);
            var root = Path.GetPathRoot(dirFull);
            if (string.IsNullOrEmpty(root) || !string.Equals(root, Path.GetPathRoot(markerFull), StringComparison.OrdinalIgnoreCase))
                return (externalDirectory, markerPath);
            // Identity matches on the configured letter: nothing to do.
            if (IdentityMatches(root, markerFull, expectedDriveId))
                return (externalDirectory, markerPath);
            var relativeMarker = markerFull[root.Length..];
            var relativeDir = dirFull[root.Length..];
            foreach (var candidate in _roots())
            {
                if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)) continue;
                var candidateMarker = Path.Combine(candidate, relativeMarker);
                if (IdentityMatches(candidate, candidateMarker, expectedDriveId))
                    return (Path.Combine(candidate, relativeDir), candidateMarker);
            }
        }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException) { }
        return (externalDirectory, markerPath);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformation(string root, System.Text.StringBuilder? name, int nameLen,
        out uint serial, out uint maxComponent, out uint flags, System.Text.StringBuilder? fs, int fsLen);

    private static string? ReadVolumeSerial(string root)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return GetVolumeInformation(root, null, 0, out var s, out _, out _, null, 0) ? s.ToString("X8") : null; }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

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
                var (id, serial) = ParseIdentity(expectedDriveId);
                if (!string.Equals(File.ReadAllText(markerPath).Trim(), id, StringComparison.Ordinal))
                    return "A different drive is connected at this location (identity does not match).";
                if (serial is not null)
                {
                    var actual = _volumeSerial(root);
                    if (actual is null)
                        return "The drive volume serial could not be read, so the drive cannot be verified.";
                    if (!string.Equals(actual, serial, StringComparison.OrdinalIgnoreCase))
                        return "A different drive is connected at this location (volume serial does not match; the marker file may have been copied).";
                }
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
