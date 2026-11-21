using GHOST.API.Security;
using GHOST.Application.Abstractions.CurrentUser;
using GHOST.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

/// <summary>Stock intake corrections (Staff on their own intakes, Admin on any) and the Admin intake report.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.StaffOrAdmin)]
[Route("api/stock-intake")]
public sealed class StockIntakeController : ControllerBase
{
    private readonly StockIntakeHandler _handler;
    private readonly ICurrentUser _currentUser;

    public StockIntakeController(StockIntakeHandler handler, ICurrentUser currentUser)
    {
        _handler = handler;
        _currentUser = currentUser;
    }

    public sealed record CorrectIntakeRequest(decimal Quantity, string? Reason, Guid? ShiftId);

    [HttpPost("movements/{movementId:guid}/correct")]
    public async Task<IActionResult> Correct(Guid movementId, [FromBody] CorrectIntakeRequest request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        var result = await _handler.CorrectAsync(
            new CorrectIntakeCommand(movementId, actorId, _currentUser.IsAdmin, request.Quantity, request.Reason, request.ShiftId), ct);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : BadRequest(new { error = result.Error });
    }

    [HttpGet("report")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Report([FromQuery] DateTimeOffset rangeFrom, [FromQuery] DateTimeOffset rangeTo, CancellationToken ct)
    {
        if (rangeTo <= rangeFrom)
            return BadRequest(new { error = "rangeTo must be after rangeFrom." });

        return Ok(await _handler.ReportAsync(rangeFrom, rangeTo, ct));
    }
}
