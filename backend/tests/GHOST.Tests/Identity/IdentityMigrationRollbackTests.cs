using GHOST.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GHOST.Tests.Identity;

public sealed class IdentityMigrationRollbackTests
{
    [Fact]
    public void Down_WhenPreviousCompositeKeysExceedSqlServerLimit_ThrowsBeforeAddingOperations()
    {
        var migrationBuilder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new TestableIdentityMigration();

        var exception = Assert.Throws<InvalidOperationException>(
            () => migration.InvokeDown(migrationBuilder));

        Assert.Empty(migrationBuilder.Operations);
        Assert.Contains("forward-only", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verified pre-migration database backup", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestableIdentityMigration : FixIdentityCompositeKeyLengths
    {
        public void InvokeDown(MigrationBuilder migrationBuilder) => base.Down(migrationBuilder);
    }
}
