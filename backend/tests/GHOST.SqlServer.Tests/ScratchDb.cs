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

    // The schema allows ONE open shift and ONE open business day in the whole database (IX_Shifts_Status, IsClosed filtered index).
    // So the scenarios run one at a time (Gate) and every shift/day a scenario opens is closed again when it ends.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly List<Guid> OpenedBusinessDays = new();
    private static readonly object OpenedLock = new();

    internal static Task EnterAsync() => Gate.WaitAsync();

    /// <summary>Closes (never deletes) the shifts and business days this scenario opened, then lets the next scenario start.</summary>
    internal static async Task LeaveAsync()
    {
        try
        {
            Guid[] days;
            lock (OpenedLock) { days = OpenedBusinessDays.ToArray(); OpenedBusinessDays.Clear(); }
            foreach (var dayId in days)
            {
                await using var db = NewContext();
                var day = await db.BusinessDays.Include(d => d.Shifts).SingleAsync(d => d.Id == dayId);
                foreach (var shift in day.Shifts.Where(s => s.Status == GHOST.Domain.Enums.ShiftStatus.Open))
                    shift.Close(shift.ExpectedCash, "scratch test cleanup");
                if (!day.IsClosed)
                    day.Close();
                await db.SaveChangesAsync();
            }
        }
        finally { Gate.Release(); }
    }

    private static async Task EnsureNoOtherOpenShiftAsync(GHOSTDbContext db)
    {
        // Another open shift that this suite did not open (for example real data in the scratch DB) must never be touched:
        // fail with a clear message instead of a unique-index error.
        if (await db.Shifts.AnyAsync(s => s.Status == GHOST.Domain.Enums.ShiftStatus.Open))
            throw new InvalidOperationException("The scratch database already has an open shift. The suite does not close shifts it did not open; use a fresh GHOST_SCRATCH_* database.");
        if (await db.BusinessDays.AnyAsync(d => !d.IsClosed))
            throw new InvalidOperationException("The scratch database already has an open business day. The suite does not close days it did not open; use a fresh GHOST_SCRATCH_* database.");
    }

    /// <summary>
    /// A fresh business day (unique far-future date) with one open shift. The shift owner is a new staff user, or
    /// <paramref name="shiftOwner"/> when given (for example an Admin who is the cashier). Call between EnterAsync and LeaveAsync only.
    /// </summary>
    internal static async Task<(Guid Admin, Guid Staff, Guid ShiftId)> SeedOpenShiftAsync(decimal openingCash = 100m, Guid? shiftOwner = null)
    {
        var admin = await SeedUserAsync("adm");
        var staff = shiftOwner ?? await SeedUserAsync("stf");
        await using var db = NewContext();
        await EnsureNoOtherOpenShiftAsync(db);
        var date = DateOnly.FromDayNumber(new Random().Next(DateOnly.Parse("2100-01-01").DayNumber, DateOnly.Parse("2900-01-01").DayNumber));
        var day = new BusinessDay(date);
        var shift = day.OpenShift(staff, openingCash);
        db.BusinessDays.Add(day);
        await db.SaveChangesAsync();
        lock (OpenedLock) OpenedBusinessDays.Add(day.Id);
        return (admin, staff, shift.Id);
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
