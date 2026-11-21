using GHOST.API.Security;
using GHOST.Application.Abstractions.CurrentUser;
using GHOST.Application.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly SettingsHandler _handler;
    private readonly ICurrentUser _currentUser;

    public SettingsController(SettingsHandler handler, ICurrentUser currentUser)
    {
        _handler = handler;
        _currentUser = currentUser;
    }

    public sealed record UpdateRequest(string? Value);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await _handler.ListAsync(cancellationToken));

    /// <summary>Key looks like Alerts:DiscountPercent; the colon is valid in a path segment.</summary>
    [HttpPut("{**key}")]
    public async Task<IActionResult> Update(string key, [FromBody] UpdateRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        var result = await _handler.UpdateAsync(actorId, key, request.Value, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }
}
