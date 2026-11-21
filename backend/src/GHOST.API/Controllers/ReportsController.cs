using GHOST.API.Security;
using GHOST.API.Reports;
using GHOST.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

/// <summary>Admin-only dashboards and reports.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly IReportQueries _queries;

    public ReportsController(IReportQueries queries) => _queries = queries;

    /// <summary>Dashboard for a half-open range [from, to). Defaults to the last 24 hours.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var end = to ?? DateTimeOffset.UtcNow;
        var start = from ?? end.AddDays(-1);

        if (start >= end)
            return BadRequest(new { error = "'from' must be before 'to'." });

        if (end - start > TimeSpan.FromDays(366))
            return BadRequest(new { error = "The range cannot exceed one year." });

        return Ok(await _queries.GetDashboardAsync(start, end, cancellationToken));
    }

    /// <summary>Revenue per local calendar day. utcOffsetMinutes defaults to Cairo (+120).</summary>
    [HttpGet("daily")]
    public async Task<IActionResult> Daily(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int utcOffsetMinutes = 120,
        [FromQuery] string? format = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryRange(from, to, out var start, out var end, out var error))
            return BadRequest(new { error });

        if (utcOffsetMinutes is < -720 or > 840)
            return BadRequest(new { error = "utcOffsetMinutes is out of range." });

        var rows = await _queries.GetDailySeriesAsync(start, end, TimeSpan.FromMinutes(utcOffsetMinutes), cancellationToken);
        return IsCsv(format)
            ? Csv("daily", new[] { "Date", "Invoices", "Revenue", "GamingRevenue", "ProductRevenue" },
                rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.Date, r.Invoices, r.Revenue, r.GamingRevenue, r.ProductRevenue }))
            : Ok(rows);
    }

    [HttpGet("shifts")]
    public async Task<IActionResult> Shifts(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? format = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryRange(from, to, out var start, out var end, out var error))
            return BadRequest(new { error });

        var rows = await _queries.GetShiftReportAsync(start, end, cancellationToken);
        return IsCsv(format)
            ? Csv("shifts",
                new[] { "Staff", "OpenedAt", "ClosedAt", "Status", "Opening", "SalesCash", "Expenses", "Withdrawals", "StockPurchases", "Refunds", "Expected", "Actual", "Difference" },
                rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.StaffName, r.OpenedAt, r.ClosedAt, r.Status, r.OpeningCash, r.SalesCash, r.Expenses, r.Withdrawals, r.StockPurchases, r.Refunds, r.ExpectedCash, r.ActualCash, r.Difference }))
            : Ok(rows);
    }

    [HttpGet("employees")]
    public async Task<IActionResult> Employees(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? format = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryRange(from, to, out var start, out var end, out var error))
            return BadRequest(new { error });

        var rows = await _queries.GetEmployeeReportAsync(start, end, cancellationToken);
        return IsCsv(format)
            ? Csv("employees",
                new[] { "Staff", "Shifts", "Invoices", "Revenue", "Expenses", "Withdrawals", "CashDifferences" },
                rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.StaffName, r.Shifts, r.Invoices, r.Revenue, r.Expenses, r.Withdrawals, r.CashDifferences }))
            : Ok(rows);
    }

    [HttpGet("devices")]
    public async Task<IActionResult> Devices(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? format = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryRange(from, to, out var start, out var end, out var error))
            return BadRequest(new { error });

        var rows = await _queries.GetDeviceReportAsync(start, end, cancellationToken);
        return IsCsv(format)
            ? Csv("devices", new[] { "Device", "Category", "Sessions", "Hours", "Revenue" },
                rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.Name, r.Category, r.Sessions, r.Hours, r.Revenue }))
            : Ok(rows);
    }

    private static bool IsCsv(string? format) => string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);

    private FileContentResult Csv(string name, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
        => File(CsvWriter.Build(headers, rows), "text/csv; charset=utf-8", $"ghost-{name}-{DateTime.UtcNow:yyyyMMdd}.csv");

    private static bool TryRange(
        DateTimeOffset? from, DateTimeOffset? to, out DateTimeOffset start, out DateTimeOffset end, out string? error)
    {
        end = to ?? DateTimeOffset.UtcNow;
        start = from ?? end.AddDays(-30);
        error = null;

        if (start >= end)
            error = "'from' must be before 'to'.";
        else if (end - start > TimeSpan.FromDays(366))
            error = "The range cannot exceed one year.";

        return error is null;
    }
}
