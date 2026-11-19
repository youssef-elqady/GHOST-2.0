using GHOST.API.Authentication;
using GHOST.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GHOST.SqlServer.Tests;

public sealed class AuthenticationAuditWriterSqlServerTests
{
    [SqlServerFact]
    public async Task WriteSystemAsync_PersistsAuthenticationAuditWithoutSensitivePayload()
    {
        await using var connection = new SqlConnection(
            SqlServerTestConnection.GetRequiredConnectionString());
        await connection.OpenAsync();

        await using (var verify = connection.CreateCommand())
        {
            verify.CommandText = "SELECT DB_NAME();";
            var actualCatalog = (string?)await verify.ExecuteScalarAsync();
            Assert.Equal(SqlServerTestConnection.RequiredCatalog, actualCatalog);
        }

        var options = new DbContextOptionsBuilder<GHOSTDbContext>()
            .UseSqlServer(connection)
            .Options;

        await using var dbContext = new GHOSTDbContext(options);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        try
        {
            var targetUserId = Guid.NewGuid();
            var writer = new AuthenticationAuditWriter(dbContext);

            await writer.WriteSystemAsync(
                "Authentication.LoginFailed",
                targetUserId,
                CancellationToken.None);

            var entry = await dbContext.AuditLogs
                .AsNoTracking()
                .SingleAsync(item => item.EntityId == targetUserId.ToString());

            Assert.Equal("System", entry.ActorType);
            Assert.Null(entry.UserId);
            Assert.Equal("Authentication.LoginFailed", entry.Action);
            Assert.Equal("Authentication", entry.EntityType);
            Assert.Null(entry.OldValues);
            Assert.Null(entry.NewValues);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
