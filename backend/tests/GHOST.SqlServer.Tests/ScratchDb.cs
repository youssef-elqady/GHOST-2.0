using System.Text.RegularExpressions;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;
using GHOST.Infrastructure.Identity;
using GHOST.Infrastructure.Persistence;
using GHOST.Infrastructure.Persistence.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GHOST.SqlServer.Tests;

/// <summary>
/// Safety boundary for the scratch-database suite. Only a database named GHOST_SCRATCH_* or GHOST_IT_* is accepted.
/// The suite never creates, drops, migrates, resets or truncates anything: the database must already exist with all
/// migrations applied (scripts/scratch-db/New-ScratchDb.ps1 does that for a NEW name and refuses an existing one).
/// Every test seeds its own uniquely named rows and only appends. The connection string is never printed.
/// </summary>
internal static partial class ScratchDb
{
    internal const string EnvironmentVariableName = "GHOST_SCRATCH_TEST_CONNECTION_STRING";

    [GeneratedRegex("^GHOST_(SCRATCH|IT)_[A-Za-z0-9_]{1,40}$")]
    private static partial Regex AllowedCatalog();

    internal static string ValidateAndNormalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException($"Set {EnvironmentVariableName} to a scratch database connection string.");

        SqlConnectionStringBuilder builder;
        try { builder = new SqlConnectionStringBuilder(raw); }
        catch (ArgumentException) { throw new InvalidOperationException("The scratch connection string is invalid."); }

        if (string.IsNullOrWhiteSpace(builder.DataSource))
            throw new InvalidOperationException("The scratch connection string needs a data source.");
        if (!AllowedCatalog().IsMatch(builder.InitialCatalog ?? string.Empty))
            throw new InvalidOperationException("Scratch tests only run on a database named GHOST_SCRATCH_* or GHOST_IT_*.");
        if (!string.IsNullOrWhiteSpace(builder.AttachDBFilename) || !string.IsNullOrWhiteSpace(builder.FailoverPartner))
            throw new InvalidOperationException("AttachDBFilename and Failover Partner are not allowed.");

        return builder.ConnectionString;
    }

    internal static string ConnectionString =>
        ValidateAndNormalize(Environment.GetEnvironmentVariable(EnvironmentVariableName));

    internal static GHOSTDbContext NewContext()
        => new(new DbContextOptionsBuilder<GHOSTDbContext>().UseSqlServer(ConnectionString).Options);

    internal static async Task<Guid> SeedUserAsync(string prefix)
    {
        await using var db = NewContext();
        var name = prefix + "_" + Guid.NewGuid().ToString("N")[..10];
        var user = new ApplicationUser { UserName = name, NormalizedUserName = name.ToUpperInvariant(), FullName = name, Email = name + "@test.invalid", NormalizedEmail = (name + "@test.invalid").ToUpperInvariant() };
        db.Set<ApplicationUser>().Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>A fresh business day (unique far-future date) with one open shift for a new staff user.</summary>
    internal static async Task<(Guid Admin, Guid Staff, Guid ShiftId)> SeedOpenShiftAsync(decimal openingCash = 100m)
    {
        var admin = await SeedUserAsync("adm");
        var staff = await SeedUserAsync("stf");
        await using var db = NewContext();
        var date = DateOnly.FromDayNumber(new Random().Next(DateOnly.Parse("2100-01-01").DayNumber, DateOnly.Parse("2900-01-01").DayNumber));
        var day = new BusinessDay(date);
        var shift = day.OpenShift(staff, openingCash);
        db.BusinessDays.Add(day);
        await db.SaveChangesAsync();
        return (admin, staff, shift.Id);
    }

    /// <summary>Opens a shift for an existing user on a fresh far-future business day.</summary>
    internal static async Task OpenShiftForUserAsync(Guid userId, decimal openingCash = 100m)
    {
        await using var db = NewContext();
        var date = DateOnly.FromDayNumber(new Random().Next(DateOnly.Parse("2100-01-01").DayNumber, DateOnly.Parse("2900-01-01").DayNumber));
        var day = new BusinessDay(date);
        day.OpenShift(userId, openingCash);
        db.BusinessDays.Add(day);
        await db.SaveChangesAsync();
    }

    internal static async Task<Guid> SeedProductAsync(Guid createdBy, decimal price, decimal stock)
    {
        await using var db = NewContext();
        var product = Product.CreateActive("P_" + Guid.NewGuid().ToString("N")[..8], null, null, "Test", "pcs", price, createdBy);
        if (stock > 0)
            product.ApplyMovement(GHOST.Domain.Enums.StockMovementType.Purchase, stock, createdBy, "scratch seed", unitCost: price / 2);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    /// <summary>One handler graph per request, each with its own DbContext, exactly like separate HTTP requests.</summary>
    internal static (GHOST.Application.Sales.SaleHandler Handler, GHOSTDbContext Db) NewSaleHandler()
    {
        var db = NewContext();
        var handler = new GHOST.Application.Sales.SaleHandler(
            new SaleRepository(db), new ProductRepository(db), new ShiftRepository(db), new SessionRepository(db),
            new UnitOfWork(db), new AuditTrail(db));
        return (handler, db);
    }
}
