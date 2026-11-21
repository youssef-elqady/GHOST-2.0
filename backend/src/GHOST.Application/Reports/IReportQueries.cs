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

public sealed record DailyRow(DateOnly Date, int Invoices, decimal Revenue, decimal GamingRevenue, decimal ProductRevenue);

public sealed record ShiftReportRow(
    Guid ShiftId,
    Guid StaffUserId,
    string StaffName,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    string Status,
    decimal OpeningCash,
    decimal SalesCash,
    decimal Expenses,
    decimal Withdrawals,
    decimal StockPurchases,
    decimal Refunds,
    decimal ExpectedCash,
    decimal? ActualCash,
    decimal? Difference);

public sealed record EmployeeReportRow(
    Guid StaffUserId,
    string StaffName,
    int Shifts,
    int Invoices,
    decimal Revenue,
    decimal Expenses,
    decimal Withdrawals,
    decimal CashDifferences);

public sealed record DeviceReportRow(
    Guid DeviceId,
    string Name,
    string Category,
    int Sessions,
    decimal Hours,
    decimal Revenue);

public interface IReportQueries
{
    Task<IReadOnlyList<DailyRow>> GetDailySeriesAsync(
        DateTimeOffset from, DateTimeOffset to, TimeSpan utcOffset, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShiftReportRow>> GetShiftReportAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeReportRow>> GetEmployeeReportAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceReportRow>> GetDeviceReportAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<DashboardSummary> GetDashboardAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
