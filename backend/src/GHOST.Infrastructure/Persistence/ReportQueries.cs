using GHOST.Application.Reports;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence;

/// <summary>Read-only reporting queries for the Admin dashboard (SRS FR-60).</summary>
public sealed class ReportQueries : IReportQueries
{
    private readonly GHOSTDbContext _db;

    public ReportQueries(GHOSTDbContext db) => _db = db;

    public async Task<DashboardSummary> GetDashboardAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // Refunded invoices stay in the totals and are shown as a refund, so net revenue is explicit.
        var paidStatuses = new[] { SaleStatus.Paid, SaleStatus.Refunded };

        var sales = _db.Sales.AsNoTracking()
            .Where(s => s.PaidAt != null && s.PaidAt >= from && s.PaidAt < to && paidStatuses.Contains(s.Status));

        var invoiceCount = await sales.CountAsync(cancellationToken);
        var gross = await sales.SumAsync(s => (decimal?)s.Total, cancellationToken) ?? 0m;
        var discounts = await sales.SumAsync(s => (decimal?)s.DiscountAmount, cancellationToken) ?? 0m;

        var items = _db.Set<SaleItem>().AsNoTracking()
            .Where(i => sales.Select(s => s.Id).Contains(i.SaleId));

        var gaming = await items.Where(i => i.ItemType == SaleItemType.Gaming)
            .SumAsync(i => (decimal?)i.LineTotal, cancellationToken) ?? 0m;
        var productRevenue = await items.Where(i => i.ItemType == SaleItemType.Product)
            .SumAsync(i => (decimal?)i.LineTotal, cancellationToken) ?? 0m;
        var productCost = await items.Where(i => i.ItemType == SaleItemType.Product)
            .SumAsync(i => (decimal?)(i.Quantity * i.UnitCostSnapshot), cancellationToken) ?? 0m;

        var refunds = await _db.Set<Refund>().AsNoTracking()
            .Where(r => r.CreatedAt >= from && r.CreatedAt < to)
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;

        var cash = _db.CashTransactions.AsNoTracking().Where(c => c.CreatedAt >= from && c.CreatedAt < to);
        var expenses = await cash.Where(c => c.Type == CashTransactionType.Expense)
            .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0m;
        var withdrawals = await cash.Where(c => c.Type == CashTransactionType.Withdrawal)
            .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0m;
        var purchases = await cash.Where(c => c.Type == CashTransactionType.StockPurchase)
            .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0m;
        var purchaseReversals = await cash.Where(c => c.Type == CashTransactionType.StockPurchaseReversal)
            .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0m;

        var gamingHours = await ComputeGamingHoursAsync(from, to, cancellationToken);

        var topProducts = await items.Where(i => i.ItemType == SaleItemType.Product && i.ProductId != null)
            .GroupBy(i => new { i.ProductId, i.DescriptionSnapshot })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.DescriptionSnapshot,
                Quantity = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.LineTotal),
                Cost = g.Sum(i => i.Quantity * i.UnitCostSnapshot)
            })
            .OrderByDescending(x => x.Revenue)
            .Take(5)
            .ToListAsync(cancellationToken);

        var lowStock = await _db.Products.AsNoTracking()
            .Where(p => p.TrackStock && p.Status != ProductStatus.Disabled && p.StockQuantity <= p.WarningStock)
            .OrderBy(p => p.StockQuantity)
            .Take(20)
            .ToListAsync(cancellationToken);

        return new DashboardSummary(
            from,
            to,
            invoiceCount,
            gross,
            gaming,
            productRevenue,
            discounts,
            refunds,
            gross - refunds,
            productCost,
            productRevenue - productCost,
            expenses,
            withdrawals,
            purchases - purchaseReversals,
            gamingHours,
            await _db.Sessions.CountAsync(s => s.Status == SessionStatus.Active, cancellationToken),
            await _db.StockReceipts.CountAsync(r => r.Status == StockReceiptStatus.Pending, cancellationToken),
            await _db.Products.CountAsync(p => p.Status == ProductStatus.PendingPricing, cancellationToken),
            topProducts.Select(x => new TopProductRow(
                x.ProductId!.Value, x.DescriptionSnapshot, x.Quantity, x.Revenue, x.Revenue - x.Cost)).ToList(),
            lowStock.Select(p => new LowStockRow(
                p.Id, p.Name, p.StockQuantity, p.MinStock, p.StockLevel.ToString())).ToList());
    }

    public async Task<IReadOnlyList<DailyRow>> GetDailySeriesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan utcOffset,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.Sales.AsNoTracking()
            .Where(s => s.PaidAt != null && s.PaidAt >= from && s.PaidAt < to
                        && (s.Status == SaleStatus.Paid || s.Status == SaleStatus.Refunded))
            .Select(s => new
            {
                PaidAt = s.PaidAt!.Value,
                s.Total,
                Gaming = s.Items.Where(i => i.ItemType == SaleItemType.Gaming).Sum(i => (decimal?)i.LineTotal) ?? 0m,
                Products = s.Items.Where(i => i.ItemType == SaleItemType.Product).Sum(i => (decimal?)i.LineTotal) ?? 0m
            })
            .ToListAsync(cancellationToken);

        // Group in memory by the local calendar day so the series matches the shop's clock.
        return rows
            .GroupBy(r => DateOnly.FromDateTime(r.PaidAt.ToOffset(utcOffset).DateTime))
            .OrderBy(g => g.Key)
            .Select(g => new DailyRow(g.Key, g.Count(), g.Sum(r => r.Total), g.Sum(r => r.Gaming), g.Sum(r => r.Products)))
            .ToList();
    }

    public async Task<IReadOnlyList<ShiftReportRow>> GetShiftReportAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var shifts = await _db.Shifts.AsNoTracking()
            .Include(s => s.CashTransactions)
            .Where(s => s.OpenedAt >= from && s.OpenedAt < to)
            .OrderBy(s => s.OpenedAt)
            .ToListAsync(cancellationToken);

        var names = await StaffNamesAsync(shifts.Select(s => s.StaffUserId), cancellationToken);

        static decimal Sum(IEnumerable<CashTransaction> tx, CashTransactionType type)
            => tx.Where(t => t.Type == type).Sum(t => t.Amount);

        return shifts.Select(s => new ShiftReportRow(
            s.Id,
            s.StaffUserId,
            names.GetValueOrDefault(s.StaffUserId, string.Empty),
            s.OpenedAt,
            s.ClosedAt,
            s.Status.ToString(),
            Sum(s.CashTransactions, CashTransactionType.OpeningBalance),
            Sum(s.CashTransactions, CashTransactionType.SaleCash),
            Sum(s.CashTransactions, CashTransactionType.Expense),
            Sum(s.CashTransactions, CashTransactionType.Withdrawal),
            Sum(s.CashTransactions, CashTransactionType.StockPurchase)
                - Sum(s.CashTransactions, CashTransactionType.StockPurchaseReversal),
            Sum(s.CashTransactions, CashTransactionType.Refund),
            s.ExpectedCash,
            s.ActualCash,
            s.CashDifference)).ToList();
    }

    public async Task<IReadOnlyList<EmployeeReportRow>> GetEmployeeReportAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var shifts = await GetShiftReportAsync(from, to, cancellationToken);

        var invoices = await _db.Sales.AsNoTracking()
            .Where(s => s.PaidAt != null && s.PaidAt >= from && s.PaidAt < to
                        && (s.Status == SaleStatus.Paid || s.Status == SaleStatus.Refunded))
            .GroupBy(s => s.CreatedById)
            .Select(g => new { Staff = g.Key, Count = g.Count(), Revenue = g.Sum(s => s.Total) })
            .ToListAsync(cancellationToken);

        var staffIds = shifts.Select(s => s.StaffUserId).Union(invoices.Select(i => i.Staff)).Distinct().ToList();
        var names = await StaffNamesAsync(staffIds, cancellationToken);

        return staffIds.Select(id => new EmployeeReportRow(
                id,
                names.GetValueOrDefault(id, string.Empty),
                shifts.Count(s => s.StaffUserId == id),
                invoices.FirstOrDefault(i => i.Staff == id)?.Count ?? 0,
                invoices.FirstOrDefault(i => i.Staff == id)?.Revenue ?? 0m,
                shifts.Where(s => s.StaffUserId == id).Sum(s => s.Expenses),
                shifts.Where(s => s.StaffUserId == id).Sum(s => s.Withdrawals),
                shifts.Where(s => s.StaffUserId == id).Sum(s => s.Difference ?? 0m)))
            .OrderByDescending(r => r.Revenue)
            .ToList();
    }

    public async Task<IReadOnlyList<DeviceReportRow>> GetDeviceReportAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var devices = await _db.Devices.AsNoTracking().ToListAsync(cancellationToken);
        var sessions = await _db.Sessions.AsNoTracking()
            .Include(s => s.Segments)
            .Where(s => s.CreatedAt >= from && s.CreatedAt < to && s.Status != SessionStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var perDevice = new Dictionary<Guid, DeviceAccumulator>();

        foreach (var session in sessions)
        {
            foreach (var segment in session.Segments)
            {
                if (!perDevice.TryGetValue(segment.DeviceId, out var entry))
                {
                    entry = new DeviceAccumulator();
                    }

                var duration = segment.GetBillableDuration(now);
                entry.Sessions.Add(session.Id);
                entry.Ticks += duration.Ticks;
                // Revenue is attributed by time share at the segment's hourly rate (before the final round-up).
                entry.Revenue += (decimal)duration.TotalHours * segment.AppliedHourlyRate;
            }
        }

        return devices.Select(d =>
            {
                var has = perDevice.TryGetValue(d.Id, out var v);
                return new DeviceReportRow(
                    d.Id,
                    d.Name,
                    d.Category,
                    has ? v.Sessions.Count : 0,
                    has ? decimal.Round((decimal)TimeSpan.FromTicks(v.Ticks).TotalHours, 2) : 0m,
                    has ? decimal.Round(v.Revenue, 2) : 0m);
            })
            .OrderByDescending(r => r.Hours)
            .ToList();
    }

    private async Task<Dictionary<Guid, string>> StaffNamesAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        var list = ids.Distinct().ToList();
        return await _db.Set<ApplicationUser>().AsNoTracking()
            .Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
    }

    /// <summary>Played hours of sessions that started in the range, from billable segment time.</summary>
    private async Task<decimal> ComputeGamingHoursAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var sessions = await _db.Sessions.AsNoTracking()
            .Include(s => s.Segments)
            .Where(s => s.CreatedAt >= from && s.CreatedAt < to && s.Status != SessionStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var ticks = sessions.Sum(s => s.GetTotalBillableDuration(now).Ticks);
        return decimal.Round((decimal)TimeSpan.FromTicks(ticks).TotalHours, 2);
    }

    private sealed class DeviceAccumulator
    {
        public HashSet<Guid> Sessions { get; } = new();
        public long Ticks { get; set; }
        public decimal Revenue { get; set; }
    }
}
