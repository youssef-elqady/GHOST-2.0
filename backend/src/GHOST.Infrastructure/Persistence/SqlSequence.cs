using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace GHOST.Infrastructure.Persistence;

/// <summary>
/// Takes the next value of a SQL Server sequence.
/// <para>
/// Do NOT use <c>Database.SqlQuery&lt;long&gt;("SELECT NEXT VALUE FOR ...").SingleAsync()</c>: EF wraps the text in a derived
/// table (and adds TOP(2) for Single), and SQL Server rejects NEXT VALUE FOR inside a derived table (found on the
/// first SQL Server run, scratch suite SC03 to SC08). A plain scalar command has no wrapper.
/// </para>
/// A sequence value is never handed out twice and is never given back: a failed operation leaves a gap, never a reused number.
/// </summary>
public static class SqlSequence
{
    public const string InvoiceNumbers = "InvoiceNumbers";
    public const string InternalBarcodes = "InternalBarcodes";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { InvoiceNumbers, InternalBarcodes };

    /// <summary>The exact SQL. Only a known sequence name is accepted, so the text can never carry caller input.</summary>
    public static string BuildNextValueSql(string sequence)
    {
        if (!Known.Contains(sequence))
            throw new ArgumentException($"Unknown sequence '{sequence}'.", nameof(sequence));

        return $"SELECT NEXT VALUE FOR [dbo].[{sequence}]";
    }

    public static async Task<long> NextValueAsync(
        DatabaseFacade database, string sequence, CancellationToken cancellationToken)
    {
        var sql = BuildNextValueSql(sequence);
        var connection = database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            // Join the caller's transaction when there is one; a sequence is not rolled back either way.
            var transaction = database.CurrentTransaction?.GetDbTransaction();
            if (transaction is not null)
                command.Transaction = transaction;

            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is null or DBNull)
                throw new InvalidOperationException($"Sequence {sequence} returned no value.");

            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }
}
