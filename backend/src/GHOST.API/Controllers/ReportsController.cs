using GHOST.API.Security;
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
}
