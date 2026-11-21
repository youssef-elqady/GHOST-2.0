using System.Text.RegularExpressions;
using GHOST.Application.Backup;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence;

/// <summary>
/// BACKUP DATABASE ... WITH CHECKSUM followed by RESTORE VERIFYONLY. The path is a parameter, and the
/// database name comes from the connection string and is bracket-quoted, so neither can inject SQL.
/// The backup folder must be writable by the SQL Server service account (not the API account).
/// </summary>
public sealed partial class SqlServerBackupGateway : IDatabaseBackupGateway
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(30);

    private readonly GHOSTDbContext _db;

    public SqlServerBackupGateway(GHOSTDbContext db) => _db = db;

    public async Task<BackupFileResult> BackupAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var database = QuotedDatabaseName();
        _db.Database.SetCommandTimeout(CommandTimeout);
        // INIT: a name clash overwrites instead of appending a second backup set into the same file.
        await _db.Database.ExecuteSqlRawAsync(
            $"BACKUP DATABASE {database} TO DISK = @path WITH CHECKSUM, INIT",
            [new SqlParameter("@path", filePath)], cancellationToken);

        long? size = null;
        try
        {
            // Visible only when the API and SQL Server share a file system.
            if (File.Exists(filePath))
                size = new FileInfo(filePath).Length;
        }
        catch (IOException)
        {
        }

        return new BackupFileResult(size);
    }

    public async Task VerifyAsync(string filePath, CancellationToken cancellationToken = default)
    {
        _db.Database.SetCommandTimeout(CommandTimeout);
        await _db.Database.ExecuteSqlRawAsync(
            "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM",
            [new SqlParameter("@path", filePath)], cancellationToken);
    }

    public Task<string?> CopyToExternalAsync(
        string filePath, string externalDirectory, int keep, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
                return Task.FromResult<string?>("The backup file is not visible to the API, so it cannot be copied. Run the API on the SQL Server machine or share the backup folder.");

            Directory.CreateDirectory(externalDirectory);
            var target = Path.Combine(externalDirectory, Path.GetFileName(filePath));
            var temp = target + ".partial";
            File.Copy(filePath, temp, overwrite: true);
            // A copy only counts when its size matches the source; the file is renamed into place last.
            if (new FileInfo(temp).Length != new FileInfo(filePath).Length)
            {
                File.Delete(temp);
                return Task.FromResult<string?>("The copy size did not match the source.");
            }

            File.Move(temp, target, overwrite: true);

            // Names start with GHOST_yyyyMMdd_HHmmss so ordering by name is ordering by time.
            foreach (var old in Directory.EnumerateFiles(externalDirectory, "GHOST_*.bak")
                         .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(Math.Max(keep, 1)))
                File.Delete(old);

            return Task.FromResult<string?>(null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Task.FromResult<string?>(exception.Message);
        }
    }

    public bool TryDelete(string filePath)
    {
        try
        {
            // Not visible from the API host (SQL Server on another machine): cannot be removed from here.
            if (!File.Exists(filePath))
                return false;

            File.Delete(filePath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string QuotedDatabaseName()
    {
        var name = new SqlConnectionStringBuilder(_db.Database.GetConnectionString()).InitialCatalog;
        if (string.IsNullOrWhiteSpace(name) || !DatabaseNamePattern().IsMatch(name))
            throw new InvalidOperationException("The database name in the connection string is not usable for backup.");

        return "[" + name + "]";
    }

    [GeneratedRegex("^[A-Za-z0-9_\\-]{1,128}$")]
    private static partial Regex DatabaseNamePattern();
}
