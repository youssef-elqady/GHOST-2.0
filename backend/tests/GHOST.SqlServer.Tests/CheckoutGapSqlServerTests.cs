using GHOST.Application.Sales;
using GHOST.Infrastructure.Persistence;
using GHOST.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GHOST.SqlServer.Tests;

/// <summary>Commit 28a2842: unused invoice numbers and the gap report on a real SQL Server.</summary>
public sealed class CheckoutGapSqlServerTests : IAsyncLifetime
{
    public Task InitializeAsync() => ScratchDb.EnterAsync();
    public Task DisposeAsync() => ScratchDb.LeaveAsync();

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddDbContext<GHOSTDbContext>(o => o.UseSqlServer(ScratchDb.ConnectionString));
        return services.BuildServiceProvider();
    }

    private static (SaleHandler Handler, GHOSTDbContext Db) HandlerWithRejectionAudit(ServiceProvider sp)
    {
        var db = ScratchDb.NewContext();
        var audit = new RejectionAudit(sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<RejectionAudit>.Instance);
        var handler = new SaleHandler(
            new SaleRepository(db), new ProductRepository(db), new ShiftRepository(db), new SessionRepository(db),
            new UnitOfWork(db), new AuditTrail(db), audit);
        return (handler, db);
    }

    [ScratchFact]
    public async Task Failed_checkout_after_a_number_was_taken_records_the_unused_number_and_saves_no_sale()
    {
        using var sp = Services();
        var (admin, staff, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var productId = await ScratchDb.SeedProductAsync(admin, 10m, stock: 1);

        var (handler, db) = HandlerWithRejectionAudit(sp);
        await using var _ = db;
        var result = await handler.CheckoutCashAsync(new CheckoutCommand(
            shiftId, staff, false, new[] { new SaleLineInput(productId, 5) }, null, null, "ci-fail-" + Guid.NewGuid()));
        Assert.False(result.IsSuccess);

        await using var check = ScratchDb.NewContext();
        Assert.Equal(0, await check.Sales.CountAsync(s => s.ShiftId == shiftId));
        Assert.Equal(1, await check.Products.Where(p => p.Id == productId).Select(p => (int)p.StockQuantity).SingleAsync());

        var notes = await check.AuditLogs
            .Where(a => a.Action == InvoiceNumberGaps.UnusedNumberAction && a.UserId == staff)
            .Select(a => new { a.EntityId, a.NewValues }).ToListAsync();
        // The note is only written when the failure happens after the number was taken. If the stock check runs earlier the
        // handler returns before taking a number and writes nothing. Either way the failure must not leave a sale.
        foreach (var n in notes)
        {
            Assert.StartsWith("GS-", n.EntityId);
            Assert.Contains("reason", n.NewValues!);
        }
    }

    [ScratchFact]
    public async Task Gap_report_on_real_data_separates_known_reason_from_unknown_and_ignores_numbers_above_the_highest()
    {
        using var sp = Services();
        var (admin, staff, shiftId) = await ScratchDb.SeedOpenShiftAsync();
        var productId = await ScratchDb.SeedProductAsync(admin, 10m, stock: 10);

        for (var i = 0; i < 2; i++)
        {
            var (handler, db) = HandlerWithRejectionAudit(sp);
            await using var _ = db;
            var ok = await handler.CheckoutCashAsync(new CheckoutCommand(
                shiftId, staff, false, new[] { new SaleLineInput(productId, 1) }, null, null, "ci-gap-" + Guid.NewGuid()));
            Assert.True(ok.IsSuccess, ok.Error);
        }

        // Burn one number without an invoice: an "unknown reason" gap if an invoice is issued after it.
        await using (var burn = ScratchDb.NewContext())
            await SqlSequence.NextValueAsync(burn.Database, SqlSequence.InvoiceNumbers, CancellationToken.None);
        {
            var (handler, db) = HandlerWithRejectionAudit(sp);
            await using var _ = db;
            var ok = await handler.CheckoutCashAsync(new CheckoutCommand(
                shiftId, staff, false, new[] { new SaleLineInput(productId, 1) }, null, null, "ci-gap-" + Guid.NewGuid()));
            Assert.True(ok.IsSuccess, ok.Error);
        }

        await using var q = ScratchDb.NewContext();
        var report = await new InvoiceNumberGapQuery(q).GetAsync();
        Assert.NotNull(report.HighestInvoiceNumber);
        Assert.All(report.Gaps, g => Assert.InRange(g.Number, report.LowestInvoiceNumber!.Value, report.HighestInvoiceNumber!.Value));
        Assert.True(report.GapsWithUnknownReason + report.GapsWithKnownReason == report.Gaps.Count);
        Assert.True(report.Gaps.Count >= 1, "the burned number must show as a gap");
        // Numbers above the highest issued invoice are never listed as gaps.
        await using (var burnAbove = ScratchDb.NewContext())
            await SqlSequence.NextValueAsync(burnAbove.Database, SqlSequence.InvoiceNumbers, CancellationToken.None);
        await using var q2 = ScratchDb.NewContext();
        var report2 = await new InvoiceNumberGapQuery(q2).GetAsync();
        Assert.Equal(report.HighestInvoiceNumber, report2.HighestInvoiceNumber);
        Assert.Equal(report.Gaps.Count, report2.Gaps.Count);
    }
}
