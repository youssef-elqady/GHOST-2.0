namespace GHOST.Application.Reports;

public sealed record TopProductRow(Guid ProductId, string Name, decimal Quantity, decimal Revenue, decimal Profit);

public sealed record LowStockRow(Guid ProductId, string Name, decimal StockQuantity, decimal MinStock, string Level);

public sealed record DashboardSummary(
    DateTimeOffset From,
    DateTimeOffset To,
    int InvoiceCount,
    decimal GrossRevenue,
    decimal GamingRevenue,
    decimal ProductRevenue,
    decimal Discounts,
    decimal Refunds,
    decimal NetRevenue,
    decimal ProductCost,
    decimal ProductProfit,
    decimal Expenses,
    decimal Withdrawals,
    decimal StockPurchases,
    decimal GamingHours,
    int ActiveSessions,
    int PendingReceipts,
    int PendingPricingProducts,
    IReadOnlyList<TopProductRow> TopProducts,
    IReadOnlyList<LowStockRow> LowStock);

public interface IReportQueries
{
    Task<DashboardSummary> GetDashboardAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
