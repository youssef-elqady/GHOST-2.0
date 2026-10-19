using GHOST.Application.Sessions.StartSession;
using GHOST.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

[ApiController]
[Route("api/sessions")]
public sealed class SessionsController : ControllerBase
{
    private readonly StartSessionHandler _startSessionHandler;

    public SessionsController(StartSessionHandler startSessionHandler)
    {
        _startSessionHandler = startSessionHandler;
    }

    public sealed record StartSessionRequest(
        Guid ShiftId,
        Guid DeviceId,
        PlayType PlayType);

    [HttpPost]
    public async Task<IActionResult> Start(
        [FromBody] StartSessionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _startSessionHandler.HandleAsync(
            new StartSessionCommand(
                request.ShiftId,
                request.DeviceId,
                request.PlayType),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return StatusCode(
            StatusCodes.Status201Created,
            new { sessionId = result.Value });
    }
}
