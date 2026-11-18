using Microsoft.Data.SqlClient;

namespace GHOST.SqlServer.Tests;

public sealed class AuditLogSqlServerTests
{
    [SqlServerFact]
    public async Task DedicatedDatabase_HasAuditLogTableAndAppendOnlyTrigger()
    {
        await using var connection = await OpenDedicatedConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DB_NAME(),
                   OBJECT_ID(N'dbo.AuditLogs', N'U'),
                   OBJECT_ID(N'dbo.TR_AuditLogs_AppendOnly', N'T');
            """;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(SqlServerTestConnection.RequiredCatalog, reader.GetString(0));
        Assert.False(reader.IsDBNull(1));
        Assert.False(reader.IsDBNull(2));
    }

    [SqlServerFact]
    public Task AppendOnlyTrigger_RejectsUpdate() =>
        AssertMutationIsRejectedAsync("UPDATE dbo.AuditLogs SET [Action] = N'IntegrationTest.Changed' WHERE [Id] = @id;");

    [SqlServerFact]
    public Task AppendOnlyTrigger_RejectsDelete() =>
        AssertMutationIsRejectedAsync("DELETE FROM dbo.AuditLogs WHERE [Id] = @id;");

    private static async Task AssertMutationIsRejectedAsync(string mutationSql)
    {
        await using var connection = await OpenDedicatedConnectionAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var id = Guid.NewGuid();

            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO dbo.AuditLogs
                        ([Id], [UserId], [ActorType], [Action], [EntityType], [EntityId],
                         [OldValues], [NewValues], [At])
                    VALUES
                        (@id, NULL, N'System', N'IntegrationTest.Insert', N'IntegrationTest',
                         @entityId, NULL, NULL, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
                    """;
                insert.Parameters.AddWithValue("@id", id);
                insert.Parameters.AddWithValue("@entityId", id.ToString("N"));
                await insert.ExecuteNonQueryAsync();
            }

            await using var mutation = connection.CreateCommand();
            mutation.Transaction = transaction;
            mutation.CommandText = mutationSql;
            mutation.Parameters.AddWithValue("@id", id);

            var exception = await Assert.ThrowsAsync<SqlException>(() => mutation.ExecuteNonQueryAsync());
            Assert.Equal(51000, exception.Number);
        }
        finally
        {
            // Roll back the inserted test row; no persistent test data or cleanup DELETE is used.
            await transaction.RollbackAsync();
        }
    }

    private static async Task<SqlConnection> OpenDedicatedConnectionAsync()
    {
        var connection = new SqlConnection(SqlServerTestConnection.GetRequiredConnectionString());
        try
        {
            await connection.OpenAsync();
            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT DB_NAME();";
            var actualCatalog = (string?)await verify.ExecuteScalarAsync();

            if (!string.Equals(actualCatalog, SqlServerTestConnection.RequiredCatalog, StringComparison.Ordinal))
                throw new InvalidOperationException("SQL Server connected to an unexpected database. AUTH-02 tests stopped.");

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
