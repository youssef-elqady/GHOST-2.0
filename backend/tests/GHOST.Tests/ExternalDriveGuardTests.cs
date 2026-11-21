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

    [Fact]
    public void Marker_and_volume_serial_must_both_match()
    {
        var marker = Path.Combine(_root, ".ghost-backup-drive");
        File.WriteAllText(marker, "VENUE-DRIVE-1");
        var ok = new ExternalDriveGuard(_ => "AB12CD34", null);
        Assert.Null(ok.Check(Path.Combine(_root, "Backups"), marker, "VENUE-DRIVE-1@ab12cd34"));
        var copied = new ExternalDriveGuard(_ => "99999999", null);
        var error = copied.Check(Path.Combine(_root, "Backups2"), marker, "VENUE-DRIVE-1@AB12CD34");
        Assert.Contains("volume serial", error);
        Assert.False(Directory.Exists(Path.Combine(_root, "Backups2")));
    }

    [Fact]
    public void Unreadable_serial_is_refused_when_serial_is_required()
    {
        var marker = Path.Combine(_root, ".ghost-backup-drive");
        File.WriteAllText(marker, "VENUE-DRIVE-1");
        Assert.NotNull(new ExternalDriveGuard(_ => null, null).Check(_root, marker, "VENUE-DRIVE-1@AB12CD34"));
    }

    [Fact]
    public void Relocate_leaves_paths_unchanged_when_no_other_root_matches()
    {
        var other = Path.Combine(Path.GetTempPath(), "ghost-drive-" + Guid.NewGuid().ToString("N"));
        try
        {
            // "Configured letter" = _root (no marker there); the same drive now sits at 'other'.
            Directory.CreateDirectory(Path.Combine(other, "ghost"));
            File.WriteAllText(Path.Combine(other, "ghost", ".id"), "VENUE-DRIVE-1");
            // Fake roots: Path root of both temp dirs is "/", so emulate letters with sub-roots via reader only.
            var guard = new ExternalDriveGuard(r => "AB12CD34", () => new[] { other + Path.DirectorySeparatorChar });
            var relocated = guard.Relocate(Path.Combine(_root, "ghost", "Backups"), Path.Combine(_root, "ghost", ".id"), "VENUE-DRIVE-1@AB12CD34");
            Assert.Equal(Path.Combine(_root, "ghost", "Backups"), relocated.Directory);
        }
        finally { try { Directory.Delete(other, true); } catch { } }
    }

    [Fact]
    public void Identity_parser_splits_id_and_serial()
    {
        Assert.Equal(("A", "1F"), ExternalDriveGuard.ParseIdentity("A@1F"));
        Assert.Equal(("A", (string?)null), ExternalDriveGuard.ParseIdentity(" A "));
    }
}
