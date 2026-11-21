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
