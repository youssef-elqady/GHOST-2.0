using System.Reflection;
using GHOST.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GHOST.Tests.Persistence;

public sealed class IdentityMigrationRollbackTests
{
    [Fact]
    public void Down_WhenPreviousPrimaryKeysExceedSqlServerLimit_ThrowsBeforeAddingOperations()
    {
        var migration = new FixIdentityCompositeKeyLengths();
        var migrationBuilder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var down = typeof(FixIdentityCompositeKeyLengths).GetMethod(
            "Down",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(down);

        var exception = Assert.Throws<TargetInvocationException>(
            () => down!.Invoke(migration, new object[] { migrationBuilder }));

        Assert.IsType<NotSupportedException>(exception.InnerException);
        Assert.Empty(migrationBuilder.Operations);
    }
}
