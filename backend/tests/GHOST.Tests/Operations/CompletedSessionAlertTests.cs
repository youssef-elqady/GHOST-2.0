using GHOST.Application.Alerts;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Tests.Operations;

/// <summary>
/// Runs the real AlertQueries rule on the EF in-memory provider. This proves the rule's logic (what is listed, who is named,
/// when it disappears). It does not prove the SQL that SQL Server will run: that needs a scratch-database run.
/// </summary>
public sealed class CompletedSessionAlertTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly DateTimeOffset From = DateTimeOffset.UtcNow.AddDays(-1);
    private static readonly DateTimeOffset To = DateTimeOffset.UtcNow.AddDays(1);
    private static readonly AlertThresholds Thresholds = new(500, 50, 20, 3, 200);

    private static GHOSTDbContext NewDb(string name)
        => new(new DbContextOptionsBuilder<GHOSTDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<List<AlertDto>> Run(string name)
    {
        await using var db = NewDb(name);
        return (await new AlertQueries(db).GetAlertsAsync(From, To, Thresholds)).Where(a => a.Type == AlertTypes.CompletedSessionNotInvoiced).ToList();
    }

    private static (Shift Shift, Session Session) Completed(GHOSTDbContext db, Guid shiftOwner)
    {
        var shift = new BusinessDay(new DateOnly(2026, 11, 25)).OpenShift(shiftOwner, 100m);
        var session = new Session(shift.Id, Guid.NewGuid(), PlayType.Single, 60m);
        session.CompleteSession(shift.Id);
        db.Shifts.Add(shift);
        db.Sessions.Add(session);
        return (shift, session);
    }

    [Fact]
    public async Task CompletedSessionWithoutInvoice_IsListed_WithTheUserWhoCompletedIt_AndStaysWhateverTheRange()
    {
        var name = Guid.NewGuid().ToString("N");
        Guid sessionId;
        await using (var db = NewDb(name))
        {
            var (_, session) = Completed(db, Staff);
            db.AuditLogs.Add(AuditLog.Create("User", Other, "Session.Completed", "Session", session.Id.ToString()));
            sessionId = session.Id;
            await db.SaveChangesAsync();
        }

        var alerts = await Run(name);

        var alert = Assert.Single(alerts);
        Assert.Equal(sessionId, alert.EntityId);
        Assert.Equal("Session", alert.EntityType);
        Assert.Equal(Other, alert.StaffUserId); // the person who completed it, not the shift owner

        // A range in the far past still lists it: the alert does not depend on the range.
        await using var db2 = NewDb(name);
        var old = await new AlertQueries(db2).GetAlertsAsync(DateTimeOffset.UtcNow.AddDays(-60), DateTimeOffset.UtcNow.AddDays(-30), Thresholds);
        Assert.Contains(old, a => a.Type == AlertTypes.CompletedSessionNotInvoiced && a.EntityId == sessionId);
    }

    [Fact]
    public async Task FallsBackToTheShiftOwner_WhenNoAuditRowExists()
    {
        var name = Guid.NewGuid().ToString("N");
        await using (var db = NewDb(name))
        {
            Completed(db, Staff);
            await db.SaveChangesAsync();
        }

        Assert.Equal(Staff, Assert.Single(await Run(name)).StaffUserId);
    }

    [Fact]
    public async Task DisappearsAfterAnInvoiceIsIssued()
    {
        var name = Guid.NewGuid().ToString("N");
        await using (var db = NewDb(name))
        {
            var (shift, session) = Completed(db, Staff);
            await db.SaveChangesAsync();
            Assert.Single(await Run(name));

            var sale = Sale.CheckoutCash(shift, Staff, "GS-2026-00001", Array.Empty<Sale.ProductLine>(), new Sale.GamingLine(session, "Gaming session"), null, "k-alert");
            db.Sales.Add(sale);
            await db.SaveChangesAsync();
        }

        Assert.Empty(await Run(name));
    }

    [Fact]
    public async Task CancelledAndActiveSessions_AreNotListed()
    {
        var name = Guid.NewGuid().ToString("N");
        await using (var db = NewDb(name))
        {
            var shift = new BusinessDay(new DateOnly(2026, 11, 25)).OpenShift(Staff, 100m);
            db.Shifts.Add(shift);
            db.Sessions.Add(new Session(shift.Id, Guid.NewGuid(), PlayType.Single, 60m)); // still active
            var cancelled = new Session(shift.Id, Guid.NewGuid(), PlayType.Single, 60m);
            cancelled.CancelSession(shift.Id, "test");
            db.Sessions.Add(cancelled);
            await db.SaveChangesAsync();
        }

        Assert.Empty(await Run(name));
    }
}
