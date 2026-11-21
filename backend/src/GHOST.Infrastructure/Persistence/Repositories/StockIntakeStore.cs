using GHOST.Application.Inventory;
using GHOST.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence.Repositories;

public sealed class StockIntakeStore : IStockIntakeStore
{
    private readonly GHOSTDbContext _db;

    public StockIntakeStore(GHOSTDbContext db) => _db = db;

    public Task<IntakeMovementInfo?> GetMovementAsync(Guid movementId, CancellationToken cancellationToken = default)
        => _db.StockMovements.AsNoTracking()
            .Where(m => m.Id == movementId)
            .Select(m => new IntakeMovementInfo(m.Id, m.ProductId, m.Type, m.QtyDelta, m.UserId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<decimal> GetCorrectedQuantityAsync(Guid movementId, CancellationToken cancellationToken = default)
    {
        var deltas = await _db.StockMovements.AsNoTracking()
            .Where(m => m.Type == StockMovementType.IntakeCorrection && m.SourceType == "StockMovement" && m.SourceId == movementId)
            .Select(m => m.QtyDelta)
            .ToListAsync(cancellationToken);
        return -deltas.Sum();
    }

    public async Task<IReadOnlyList<IntakeReportRow>> GetIntakeReportAsync(
        DateTimeOffset rangeFrom, DateTimeOffset rangeTo, CancellationToken cancellationToken = default)
    {
        var rows = await _db.StockMovements.AsNoTracking()
            .Where(m => (m.Type == StockMovementType.Purchase || m.Type == StockMovementType.IntakeCorrection)
                        && m.CreatedAt >= rangeFrom && m.CreatedAt < rangeTo)
            .Join(_db.Products.AsNoTracking(), m => m.ProductId, p => p.Id, (m, p) => new { m.UserId, m.ProductId, p.Name, m.Type, m.QtyDelta, m.UnitCost })
            .ToListAsync(cancellationToken);

        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName, cancellationToken);

        return rows
            .GroupBy(r => new { r.UserId, r.ProductId, r.Name })
            .Select(g => new IntakeReportRow(
                g.Key.UserId, names.GetValueOrDefault(g.Key.UserId), g.Key.ProductId, g.Key.Name,
                g.Count(r => r.Type == StockMovementType.Purchase),
                g.Sum(r => r.QtyDelta),
                g.Sum(r => r.QtyDelta * r.UnitCost)))
            .OrderBy(r => r.UserName ?? "").ThenBy(r => r.UserId).ThenBy(r => r.ProductName)
            .ToList();
    }
}
