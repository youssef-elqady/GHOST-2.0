using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GHOST.SqlServer.Tests;

/// <summary>Helpers for the CI SQL Server tests. They only append rows to the guarded GHOST_IT_*/GHOST_SCRATCH_* database.</summary>
internal static class CiSupport
{
    /// <summary>A new Active session whose first segment and the session itself started <paramref name="ago"/> before now.</summary>
    internal static async Task<(Guid SessionId, DateTimeOffset Start)> NewSessionStartedAsync(Guid shiftId, TimeSpan ago)
    {
        var start = DateTimeOffset.UtcNow - ago;
        Guid sessionId;
        await using (var db = ScratchDb.NewContext())
        {
            var device = new Device("CI-" + Guid.NewGuid().ToString("N")[..8], "PS5");
            db.Devices.Add(device);
            var session = new Session(shiftId, device.Id, PlayType.Single, 60m);
            db.Sessions.Add(session);
            await db.SaveChangesAsync();
            sessionId = session.Id;
        }

        await using (var db = ScratchDb.NewContext())
        {
            // Test-only seeding on the throwaway CI database: put the start in the past so outage windows fit inside the session.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.SessionSegments SET StartedAt = {start} WHERE SessionId = {sessionId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Sessions SET CreatedAt = {start} WHERE Id = {sessionId}");
        }

        return (sessionId, start);
    }

    internal static async Task<Session> LoadSessionAsync(GHOST.Infrastructure.Persistence.GHOSTDbContext db, Guid id)
        => await db.Sessions.Include(s => s.Segments).ThenInclude(s => s.Pauses).SingleAsync(s => s.Id == id);

    /// <summary>Marks an outage on a freshly loaded session and saves, like one recovery pass in its own request.</summary>
    internal static async Task MarkOutageAsync(Guid sessionId, DateTimeOffset last, DateTimeOffset recovered)
    {
        await using var db = ScratchDb.NewContext();
        var s = await LoadSessionAsync(db, sessionId);
        s.MarkPowerOutage(last, recovered);
        await db.SaveChangesAsync();
    }

    internal static async Task<(int Pauses, TimeSpan Billable)> ReadAsync(Guid sessionId, DateTimeOffset upTo)
    {
        await using var db = ScratchDb.NewContext();
        var s = await LoadSessionAsync(db, sessionId);
        return (s.Segments.Sum(x => x.Pauses.Count), s.GetTotalBillableDuration(upTo));
    }
}
