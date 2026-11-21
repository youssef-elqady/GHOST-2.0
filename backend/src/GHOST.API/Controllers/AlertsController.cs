using GHOST.API.Security;
using GHOST.Application.Alerts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/alerts")]
public sealed class AlertsController : ControllerBase
{
    private readonly AlertsHandler _handler;

    public AlertsController(AlertsHandler handler) => _handler = handler;

    /// <summary>Alerts in a range (default last 7 days, max 92). Newest first.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var result = await _handler.GetAsync(from, to, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }
}
