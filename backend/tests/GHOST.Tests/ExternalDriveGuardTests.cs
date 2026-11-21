using GHOST.Infrastructure.Persistence;

namespace GHOST.Tests;

public sealed class ExternalDriveGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghost-drive-" + Guid.NewGuid().ToString("N"));
    private readonly ExternalDriveGuard _guard = new();

    public ExternalDriveGuardTests() => Directory.CreateDirectory(_root);

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Matching_marker_and_writable_directory_is_accepted()
    {
        var marker = Path.Combine(_root, ".ghost-backup-drive");
        File.WriteAllText(marker, "VENUE-DRIVE-1\n");
        Assert.Null(_guard.Check(Path.Combine(_root, "Backups"), marker, "VENUE-DRIVE-1"));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Backups")));
    }

    [Fact]
    public void Missing_marker_means_wrong_or_absent_drive()
    {
        var error = _guard.Check(Path.Combine(_root, "Backups"), Path.Combine(_root, ".ghost-backup-drive"), "X");
        Assert.NotNull(error);
        Assert.False(Directory.Exists(Path.Combine(_root, "Backups")));
    }

    [Fact]
    public void Different_marker_content_is_refused()
    {
        var marker = Path.Combine(_root, ".ghost-backup-drive");
        File.WriteAllText(marker, "OTHER-DRIVE");
        var error = _guard.Check(Path.Combine(_root, "Backups"), marker, "VENUE-DRIVE-1");
        Assert.Contains("different drive", error);
        Assert.False(Directory.Exists(Path.Combine(_root, "Backups")));
    }

    [Fact]
    public void Id_without_marker_path_is_a_configuration_error()
        => Assert.NotNull(_guard.Check(_root, null, "X"));

    [Fact]
    public void Unwritable_destination_is_reported()
    {
        var blocker = Path.Combine(_root, "file-not-folder");
        File.WriteAllText(blocker, "x");
        Assert.NotNull(_guard.Check(Path.Combine(blocker, "Backups"), null, null));
    }

    [Fact]
    public void No_identity_configured_still_checks_presence_and_writability()
        => Assert.Null(_guard.Check(Path.Combine(_root, "Backups"), null, null));
}
