using System.Reflection;
using System.Text.RegularExpressions;
using GHOST.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GHOST.Tests.Persistence;

/// <summary>
/// Regression for a real bug: two migrations both ran CREATE TRIGGER with the same name, which fails on a clean
/// SQL Server database. This replays every migration's Up() into a MigrationBuilder (no database involved) and
/// checks that no trigger name is created twice across the whole chain.
/// </summary>
public sealed class MigrationTriggerTests
{
    private static readonly Regex CreateTrigger = new(@"CREATE\s+TRIGGER\s+\[?dbo\]?\.\[?(?<name>[A-Za-z0-9_]+)\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DropTrigger = new(@"DROP\s+TRIGGER\s+(IF\s+EXISTS\s+)?\[?dbo\]?\.\[?(?<name>[A-Za-z0-9_]+)\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static IEnumerable<(string Migration, string Trigger)> CreatedTriggers() => Triggers("Up", CreateTrigger);

    private static IEnumerable<(string Migration, string Trigger)> DroppedTriggers() => Triggers("Down", DropTrigger);

    private static IEnumerable<(string Migration, string Trigger)> Triggers(string method, Regex pattern)
    {
        var migrationTypes = typeof(GHOSTDbContext).Assembly.GetTypes()
            .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract && t.GetCustomAttribute<MigrationAttribute>() is not null)
            .OrderBy(t => t.GetCustomAttribute<MigrationAttribute>()!.Id, StringComparer.Ordinal);

        foreach (var type in migrationTypes)
        {
            var migration = (Migration)Activator.CreateInstance(type)!;
            var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
            try
            {
                type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);
            }
            catch (TargetInvocationException ex) when (method == "Down" && ex.InnerException is InvalidOperationException)
            {
                continue; // deliberately forward-only migration: its Down() throws and drops nothing
            }
            foreach (var sql in builder.Operations.OfType<SqlOperation>())
                foreach (Match m in pattern.Matches(sql.Sql))
                    yield return (type.Name, m.Groups["name"].Value);
        }
    }

    [Fact]
    public void Migrations_create_at_least_the_known_append_only_triggers()
    {
        var names = CreatedTriggers().Select(t => t.Trigger).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("TR_AuditLogs_AppendOnly", names);
        Assert.Contains("TR_CashTransactions_AppendOnly", names);
        Assert.Contains("TR_StockMovements_AppendOnly", names);
        Assert.Contains("TR_ProductPriceHistory_AppendOnly", names);
    }

    [Fact]
    public void No_trigger_name_is_created_by_more_than_one_migration()
    {
        var duplicates = CreatedTriggers()
            .GroupBy(t => t.Trigger, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} created by: {string.Join(", ", g.Select(x => x.Migration))}")
            .ToList();

        Assert.True(duplicates.Count == 0, string.Join("; ", duplicates));
    }

    [Fact]
    public void A_migration_only_drops_triggers_it_created_itself()
    {
        var created = CreatedTriggers().ToHashSet();
        var foreign = DroppedTriggers()
            .Where(d => !created.Contains((d.Migration, d.Trigger)))
            .Select(d => $"{d.Migration} drops {d.Trigger} which it did not create")
            .ToList();

        Assert.True(foreign.Count == 0, string.Join("; ", foreign));
    }

    [Fact]
    public void No_trigger_name_is_dropped_by_more_than_one_migration()
    {
        var duplicates = DroppedTriggers()
            .GroupBy(t => t.Trigger, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} dropped by: {string.Join(", ", g.Select(x => x.Migration))}")
            .ToList();

        Assert.True(duplicates.Count == 0, string.Join("; ", duplicates));
    }

    [Fact]
    public void Down_of_every_migration_that_creates_a_trigger_drops_it()
    {
        var dropped = DroppedTriggers().ToHashSet();
        var missing = CreatedTriggers()
            .Where(c => !dropped.Contains(c))
            .Select(c => $"{c.Migration} creates {c.Trigger} but its Down() does not drop it")
            .ToList();

        Assert.True(missing.Count == 0, string.Join("; ", missing));
    }
}
