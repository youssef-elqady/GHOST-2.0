using GHOST.API.Security;
using GHOST.Application.Abstractions.CurrentUser;
using GHOST.Application.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

/// <summary>Admin review of sessions that were running during a power cut.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/sessions/outage-review")]
public sealed class OutageReviewController : ControllerBase
{
    private readonly PowerOutageHandler _handler;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;

    public OutageReviewController(PowerOutageHandler handler, ICurrentUser currentUser, TimeProvider time)
    {
        _handler = handler;
        _currentUser = currentUser;
        _time = time;
    }

    public sealed record ApproveOutageRequest(Guid EndShiftId, DateTimeOffset ActualEndAt, string? Note);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _handler.ListNeedingReviewAsync(_time.GetUtcNow(), ct));

    [HttpPost("{sessionId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid sessionId, [FromBody] ApproveOutageRequest request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } adminId)
            return Unauthorized();

        var result = await _handler.ApproveAsync(
            new ApproveOutageCommand(sessionId, request.EndShiftId, request.ActualEndAt, adminId, request.Note),
            _time.GetUtcNow(),
            ct);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }
}
