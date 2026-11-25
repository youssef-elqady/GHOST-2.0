using GHOST.Application.Alerts;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Infrastructure.Persistence;
using GHOST.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Tests.Operations;

/// <summary>EF in-memory only: proves the rule's logic, not the SQL Server query.</summary>
public sealed class RefundedSessionInvoiceTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly AlertThresholds Thresholds = new(500, 50, 20, 3, 200);

    private static GHOSTDbContext NewDb(string name)
        => new(new DbContextOptionsBuilder<GHOSTDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<(Guid SessionId, Guid SaleId)> Seed(string name, bool refund)
    {
        await using var db = NewDb(name);
        var shift = new BusinessDay(new DateOnly(2026, 11, 25)).OpenShift(Staff, 100m);
        var session = new Session(shift.Id, Guid.NewGuid(), PlayType.Single, 60m);
        session.CompleteSession(shift.Id);
        var sale = Sale.CheckoutCash(shift, Staff, "GS-2026-00001", Array.Empty<Sale.ProductLine>(), new Sale.GamingLine(session, "Gaming session"), null, "k-r1");
        if (refund)
            sale.RefundAll(Admin, "customer complaint", false, shift, new Dictionary<Guid, Product>());
        db.Shifts.Add(shift);
        db.Sessions.Add(session);
        db.Sales.Add(sale);
        await db.SaveChangesAsync();
        return (session.Id, sale.Id);
    }

    [Fact]
    public async Task RefundedSessionInvoice_StillCountsAsInvoiced_SoItCannotBeInvoicedAgain()
    {
        var name = Guid.NewGuid().ToString("N");
        var (sessionId, _) = await Seed(name, refund: true);
        await using var db = NewDb(name);
        Assert.True(await new SaleRepository(db).SessionIsInvoicedAsync(sessionId));
    }

    [Fact]
    public async Task RefundedSessionInvoice_ShowsAnAdminAlert_NamingTheAdminWhoRefunded()
    {
        var name = Guid.NewGuid().ToString("N");
        var (sessionId, _) = await Seed(name, refund: true);
        await using var db = NewDb(name);
        var alerts = (await new AlertQueries(db).GetAlertsAsync(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-20), Thresholds))
            .Where(a => a.Type == AlertTypes.RefundedSessionInvoice).ToList();

        var alert = Assert.Single(alerts); // range-independent
        Assert.Equal(sessionId, alert.EntityId);
        Assert.Equal(Admin, alert.StaffUserId);
        Assert.True(alert.Amount > 0m); // the amount that went back
    }

    [Fact]
    public async Task PaidSessionInvoice_GivesNoRefundAlert()
    {
        var name = Guid.NewGuid().ToString("N");
        await Seed(name, refund: false);
        await using var db = NewDb(name);
        var alerts = await new AlertQueries(db).GetAlertsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), Thresholds);
        Assert.DoesNotContain(alerts, a => a.Type == AlertTypes.RefundedSessionInvoice);
    }
}
