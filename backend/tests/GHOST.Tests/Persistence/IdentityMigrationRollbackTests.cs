using GHOST.Infrastructure.Persistence.Migrations;

namespace GHOST.Tests.Persistence;

public sealed class IdentityMigrationRollbackTests
{
    [Fact]
    public void DownOperations_WhenRequested_ThrowsBeforeCreatingSchemaOperations()
    {
        var migration = new FixIdentityCompositeKeyLengths();

        var exception = Assert.Throws<NotSupportedException>(
            () => _ = migration.DownOperations);

        Assert.Contains("forward-only", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verified pre-migration database backup", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("900-byte limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
