using GHOST.Infrastructure.Persistence;

namespace GHOST.Tests.Operations;

/// <summary>Real file-system test of the external-drive copy (no database involved).</summary>
public sealed class ExternalBackupCopyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghost-ext-" + Guid.NewGuid().ToString("N"));
    private readonly string _src;
    private readonly string _ext;

    public ExternalBackupCopyTests()
    {
        _src = Path.Combine(_root, "src");
        _ext = Path.Combine(_root, "usb");
        Directory.CreateDirectory(_src);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Make(string stamp)
    {
        var path = Path.Combine(_src, $"GHOST_{stamp}_Scheduled.bak");
        File.WriteAllText(path, "data-" + stamp);
        return path;
    }

    [Fact]
    public async Task Copies_the_file_and_keeps_only_the_newest_N()
    {
        var gateway = new SqlServerBackupGateway(null!);
        foreach (var stamp in new[] { "20261101_030000", "20261102_030000", "20261103_030000" })
            Assert.Null(await gateway.CopyToExternalAsync(Make(stamp), _ext, keep: 2));

        var names = Directory.GetFiles(_ext).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(["GHOST_20261102_030000_Scheduled.bak", "GHOST_20261103_030000_Scheduled.bak"], names);
        Assert.Equal("data-20261103_030000", File.ReadAllText(Path.Combine(_ext, names[1]!)));
        Assert.DoesNotContain(Directory.GetFiles(_ext), f => f.EndsWith(".partial"));
    }

    [Fact]
    public async Task Missing_source_reports_an_error_instead_of_throwing()
    {
        var error = await new SqlServerBackupGateway(null!).CopyToExternalAsync(Path.Combine(_src, "nope.bak"), _ext, 7);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Unusable_destination_reports_an_error()
    {
        var blocker = Path.Combine(_root, "file-not-dir");
        File.WriteAllText(blocker, "x");
        var error = await new SqlServerBackupGateway(null!).CopyToExternalAsync(Make("20261104_030000"), Path.Combine(blocker, "sub"), 7);
        Assert.NotNull(error);
    }
}
