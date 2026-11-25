using System.Data;
using GHOST.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GHOST.SqlServer.Tests;

/// <summary>Commit 28a2842: SqlSequence against a real SQL Server.</summary>
public sealed class SqlSequenceSqlServerTests
{
    [ScratchFact]
    public async Task Sequential_values_from_both_sequences_are_unique_and_increasing()
    {
        foreach (var name in new[] { SqlSequence.InvoiceNumbers, SqlSequence.InternalBarcodes })
        {
            await using var db = ScratchDb.NewContext();
            var values = new List<long>();
            for (var i = 0; i < 10; i++)
                values.Add(await SqlSequence.NextValueAsync(db.Database, name, CancellationToken.None));
            Assert.Equal(10, values.Distinct().Count());
            Assert.Equal(values.OrderBy(x => x), values);
        }
    }

    [ScratchFact]
    public async Task Fifty_concurrent_requests_never_get_the_same_value()
    {
        var tasks = Enumerable.Range(0, 50).Select(async _ =>
        {
            await using var db = ScratchDb.NewContext();
            return await SqlSequence.NextValueAsync(db.Database, SqlSequence.InvoiceNumbers, CancellationToken.None);
        }).ToArray();
        var values = await Task.WhenAll(tasks);
        Assert.Equal(50, values.Distinct().Count());
    }

    [ScratchFact]
    public async Task Value_taken_inside_a_transaction_is_not_given_back_by_a_rollback()
    {
        await using var db = ScratchDb.NewContext();
        long inside;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            inside = await SqlSequence.NextValueAsync(db.Database, SqlSequence.InvoiceNumbers, CancellationToken.None);
            await tx.RollbackAsync();
        }

        var after = await SqlSequence.NextValueAsync(db.Database, SqlSequence.InvoiceNumbers, CancellationToken.None);
        Assert.True(after > inside, $"after={after} must be greater than the rolled back value {inside}");
    }

    [ScratchFact]
    public async Task Connection_already_open_stays_open_and_a_helper_opened_one_is_closed()
    {
        await using var db = ScratchDb.NewContext();
        var connection = db.Database.GetDbConnection();

        Assert.Equal(ConnectionState.Closed, connection.State);
        await SqlSequence.NextValueAsync(db.Database, SqlSequence.InternalBarcodes, CancellationToken.None);
        Assert.Equal(ConnectionState.Closed, connection.State);

        await connection.OpenAsync();
        await SqlSequence.NextValueAsync(db.Database, SqlSequence.InternalBarcodes, CancellationToken.None);
        Assert.Equal(ConnectionState.Open, connection.State);
    }

    [ScratchFact]
    public async Task Unknown_sequence_name_is_refused_before_any_sql_runs_and_real_sql_is_valid()
    {
        await using var db = ScratchDb.NewContext();
        await Assert.ThrowsAsync<ArgumentException>(() => SqlSequence.NextValueAsync(db.Database, "InvoiceNumbers]; DROP TABLE x;--", CancellationToken.None));
        Assert.Equal("SELECT NEXT VALUE FOR [dbo].[InvoiceNumbers]", SqlSequence.BuildNextValueSql(SqlSequence.InvoiceNumbers));
        // The real SQL Server executed the statement above in the tests before this one; run it once more directly.
        Assert.True(await SqlSequence.NextValueAsync(db.Database, SqlSequence.InvoiceNumbers, CancellationToken.None) > 0);
    }
}
