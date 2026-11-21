using GHOST.Application.Sales;
using GHOST.Application.Sessions;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Infrastructure.Persistence;
using GHOST.Infrastructure.Persistence.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GHOST.SqlServer.Tests;

/// <summary>
/// Real SQL Server tests on a scratch database. Test IDs (SC-xx) map to docs/engineering/GHOST-TEST-STRATEGY.md.
/// Every scenario uses new rows only and never deletes. Concurrency tests use one DbContext per request.
/// </summary>
public sealed class ScratchSuiteTests
{
    // SC-01 All migrations are applied.
    [ScratchFact]
    public async Task SC01_all_migrations_are_applied()
    {
        await using var db = ScratchDb.NewContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
    }

    // SC-02 Append-only triggers on the cash ledger, stock movements and the audit log reject UPDATE and DELETE (statement triggers fire
    // even when no row matches, so no data is needed; everything runs in a transaction that is rolled back).
    [ScratchFact]
    public async Task SC02_append_only_triggers_reject_update_and_delete()
    {
        foreach (var statement in new[]
        {
            "UPDATE dbo.CashTransactions SET Amount = Amount WHERE 1 = 0",
            "DELETE FROM dbo.CashTransactions WHERE 1 = 0",
            "UPDATE dbo.AuditLogs SET Action = Action WHERE 1 = 0",
            "DELETE FROM dbo.AuditLogs WHERE 1 = 0",
            "UPDATE dbo.StockMovements SET QtyDelta = QtyDelta WHERE 1 = 0",
            "DELETE FROM dbo.StockMovements WHERE 1 = 0",
        })
        {
            await using var connection = new SqlConnection(ScratchDb.ConnectionString);
            await connection.OpenAsync();
            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = statement;
            var error = await Assert.ThrowsAsync<SqlException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Contains("append-only", error.Message, StringComparison.OrdinalIgnoreCase);
            try { await tx.RollbackAsync(); } catch (InvalidOperationException) { }
        }
    }

    // SC-03 Two simultaneous checkouts of the LAST unit: exactly one wins, stock is never negative or deducted twice.
    [ScratchFact]
    public async Task SC03_two_concurrent_sales_of_the_last_unit_sell_it_once()
    {
        var (admin, staff, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var productId = await ScratchDb.SeedProductAsync(admin, 20m, stock: 1);

        async Task<bool> Sell(string key)
        {
            var (handler, db) = ScratchDb.NewSaleHandler();
            await using var _ = db;
            var result = await handler.CheckoutCashAsync(new CheckoutCommand(
                shiftId, staff, false, new[] { new SaleLineInput(productId, 1) }, null, null, key));
            return result.IsSuccess;
        }

        var results = await Task.WhenAll(Sell("sc03-a-" + Guid.NewGuid()), Sell("sc03-b-" + Guid.NewGuid()));

        Assert.Equal(1, results.Count(r => r));
        await using var check = ScratchDb.NewContext();
        Assert.Equal(0m, await check.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync());
        Assert.Equal(1, await check.Sales.CountAsync(s => s.ShiftId == shiftId));
    }

    // SC-04 Many simultaneous sales against limited stock: sold units == stock removed, never below zero.
    [ScratchFact]
    public async Task SC04_stress_20_concurrent_sales_of_5_units()
    {
        var (admin, staff, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var productId = await ScratchDb.SeedProductAsync(admin, 10m, stock: 5);

        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            var (handler, db) = ScratchDb.NewSaleHandler();
            await using var _ = db;
            var r = await handler.CheckoutCashAsync(new CheckoutCommand(
                shiftId, staff, false, new[] { new SaleLineInput(productId, 1) }, null, null, "sc04-" + i + "-" + Guid.NewGuid()));
            return r.IsSuccess;
        }).ToArray();
        var ok = (await Task.WhenAll(tasks)).Count(x => x);

        await using var check = ScratchDb.NewContext();
        var stock = await check.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync();
        Assert.InRange(ok, 1, 5);
        Assert.Equal(5 - ok, stock);
        Assert.True(stock >= 0);
        Assert.Equal(ok, await check.Sales.CountAsync(s => s.ShiftId == shiftId));
    }

    // SC-05 The same checkout sent 8 times at once (double tap / retry after timeout): one invoice, one stock deduction.
    [ScratchFact]
    public async Task SC05_same_key_concurrently_creates_one_invoice()
    {
        var (admin, staff, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var productId = await ScratchDb.SeedProductAsync(admin, 20m, stock: 10);
        var key = "sc05-" + Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            var (handler, db) = ScratchDb.NewSaleHandler();
            await using var __ = db;
            return await handler.CheckoutCashAsync(new CheckoutCommand(
                shiftId, staff, false, new[] { new SaleLineInput(productId, 2) }, null, null, key));
        }));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        Assert.Single(results.Select(r => r.Value!.Id).Distinct());
        await using var check = ScratchDb.NewContext();
        Assert.Equal(8m, await check.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync());
        Assert.Equal(1, await check.Sales.CountAsync(s => s.ShiftId == shiftId));
    }

    // SC-06 Two separate purchases with identical cart content and different keys are both created.
    [ScratchFact]
    public async Task SC06_identical_carts_with_different_keys_are_two_invoices()
    {
        var (admin, staff, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var productId = await ScratchDb.SeedProductAsync(admin, 20m, stock: 10);

        for (var i = 0; i < 2; i++)
        {
            var (handler, db) = ScratchDb.NewSaleHandler();
            await using var _ = db;
            var r = await handler.CheckoutCashAsync(new CheckoutCommand(
                shiftId, staff, false, new[] { new SaleLineInput(productId, 2) }, null, null, "sc06-" + i + "-" + Guid.NewGuid()));
            Assert.True(r.IsSuccess, r.Error);
        }

        await using var check = ScratchDb.NewContext();
        Assert.Equal(2, await check.Sales.CountAsync(s => s.ShiftId == shiftId));
        Assert.Equal(6m, await check.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync());
    }

    // SC-07 Same key with different content is rejected.
    [ScratchFact]
    public async Task SC07_same_key_different_cart_is_rejected()
    {
        var (admin, staff, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var productId = await ScratchDb.SeedProductAsync(admin, 20m, stock: 10);
        var key = "sc07-" + Guid.NewGuid();

        var (h1, d1) = ScratchDb.NewSaleHandler();
        await using (d1)
            Assert.True((await h1.CheckoutCashAsync(new CheckoutCommand(shiftId, staff, false, new[] { new SaleLineInput(productId, 1) }, null, null, key))).IsSuccess);
        var (h2, d2) = ScratchDb.NewSaleHandler();
        await using (d2)
            Assert.False((await h2.CheckoutCashAsync(new CheckoutCommand(shiftId, staff, false, new[] { new SaleLineInput(productId, 3) }, null, null, key))).IsSuccess);

        await using var check = ScratchDb.NewContext();
        Assert.Equal(9m, await check.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync());
    }

    // SC-08 Two admins refund the same invoice at the same time: one refund, stock returned once.
    [ScratchFact]
    public async Task SC08_double_refund_by_two_admins_refunds_once()
    {
        var (admin, staff, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var admin2 = await ScratchDb.SeedUserAsync("adm2");
        await ScratchDb.OpenShiftForUserAsync(admin);
        await ScratchDb.OpenShiftForUserAsync(admin2);
        var productId = await ScratchDb.SeedProductAsync(admin, 20m, stock: 10);

        var (sh, sdb) = ScratchDb.NewSaleHandler();
        SaleDto sale;
        await using (sdb)
            sale = (await sh.CheckoutCashAsync(new CheckoutCommand(shiftId, staff, false, new[] { new SaleLineInput(productId, 4) }, null, null, "sc08-" + Guid.NewGuid()))).Value!;

        async Task<bool> Refund(Guid who)
        {
            var (handler, db) = ScratchDb.NewSaleHandler();
            await using var _ = db;
            return (await handler.RefundAsync(sale.Id, who, "scratch test", true, null)).IsSuccess;
        }

        var results = await Task.WhenAll(Refund(admin), Refund(admin2));

        Assert.Equal(1, results.Count(r => r));
        await using var check = ScratchDb.NewContext();
        Assert.Equal(10m, await check.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync());
    }

    // SC-09 Power cut: active session is flagged after a long heartbeat gap, outage is not billed, Admin approves once, all audited.
    [ScratchFact]
    public async Task SC09_power_cut_flow_on_real_database()
    {
        var (admin, staff, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        Guid sessionId;
        await using (var db = ScratchDb.NewContext())
        {
            var device = new Device("PS5-" + Guid.NewGuid().ToString("N")[..6], "PS5");
            db.Devices.Add(device);
            var session = new Session(shiftId, device.Id, PlayType.Single, 60m);
            db.Sessions.Add(session);
            await db.SaveChangesAsync();
            sessionId = session.Id;
        }

        PowerOutageHandler Handler(GHOSTDbContext db) => new(
            new OutageRepository(db), new SessionRepository(db), new UnitOfWork(db), new AuditTrail(db), new SystemAuditTrail(db));

        // The system "was running" 3 hours ago and comes back now. The singleton heartbeat row is set directly (scratch DB only).
        var lastBeat = DateTimeOffset.UtcNow.AddHours(-3);
        await using (var seed = ScratchDb.NewContext())
        {
            await new OutageRepository(seed).SaveHeartbeatAsync(lastBeat);
            await seed.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.SystemHeartbeats SET BeatAt = {lastBeat} WHERE Id = 1");
        }
        await using (var db = ScratchDb.NewContext())
        {
            var r = await Handler(db).RecoverAsync(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
            Assert.True(r.OutageDetected);
        }

        await using (var db = ScratchDb.NewContext())
        {
            var s = await db.Sessions.Include(x => x.Segments).ThenInclude(x => x.Pauses).SingleAsync(x => x.Id == sessionId);
            Assert.True(s.NeedsReview);
            Assert.Single(s.GetActiveSegment()!.Pauses);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == sessionId.ToString() && a.Action == "Session.PowerOutageDetected"));
        }

        var end = DateTimeOffset.UtcNow.AddMinutes(-1);
        await using (var db = ScratchDb.NewContext())
        {
            var ok = await Handler(db).ApproveAsync(new ApproveOutageCommand(sessionId, shiftId, end, admin, "test"), DateTimeOffset.UtcNow);
            Assert.True(ok.IsSuccess, ok.Error);
        }
        await using (var db = ScratchDb.NewContext())
        {
            var again = await Handler(db).ApproveAsync(new ApproveOutageCommand(sessionId, shiftId, end, admin, "dup"), DateTimeOffset.UtcNow);
            Assert.False(again.IsSuccess);
            Assert.Equal(SessionStatus.Completed, await db.Sessions.Where(x => x.Id == sessionId).Select(x => x.Status).SingleAsync());
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityId == sessionId.ToString() && a.Action == "Session.OutageApproved"));
        }
    }
}
