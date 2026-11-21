using GHOST.Application.Reports;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
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
}
