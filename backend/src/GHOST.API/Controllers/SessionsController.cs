using GHOST.Application.Sessions;
using GHOST.Application.Sessions.StartSession;
using GHOST.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

[ApiController]
[Route("api/sessions")]
public sealed class SessionsController : ControllerBase
{
    private readonly StartSessionHandler _startSessionHandler;
    private readonly SessionLifecycleHandler _lifecycleHandler;
    private readonly TransferSessionHandler _transferSessionHandler;

    public SessionsController(
        StartSessionHandler startSessionHandler,
        SessionLifecycleHandler lifecycleHandler,
        TransferSessionHandler transferSessionHandler)
    {
        _startSessionHandler = startSessionHandler;
        _lifecycleHandler = lifecycleHandler;
        _transferSessionHandler = transferSessionHandler;
    }

    public sealed record StartSessionRequest(
        Guid ShiftId,
        Guid DeviceId,
        PlayType PlayType);

    public sealed record CompleteSessionRequest(Guid EndShiftId);

    public sealed record TransferSessionRequest(Guid NewDeviceId, PlayType PlayType);

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
            return BadRequest(new { error = result.Error });

        return StatusCode(
            StatusCodes.Status201Created,
            new { sessionId = result.Value });
    }

    [HttpPost("{sessionId:guid}/pause")]
    public async Task<IActionResult> Pause(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await _lifecycleHandler.PauseAsync(sessionId, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new { error = result.Error });
    }

    [HttpPost("{sessionId:guid}/resume")]
    public async Task<IActionResult> Resume(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await _lifecycleHandler.ResumeAsync(sessionId, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new { error = result.Error });
    }

    [HttpPost("{sessionId:guid}/transfer")]
    public async Task<IActionResult> Transfer(
        Guid sessionId,
        [FromBody] TransferSessionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _transferSessionHandler.HandleAsync(
            new TransferSessionCommand(sessionId, request.NewDeviceId, request.PlayType),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new { error = result.Error });
    }

    [HttpPost("{sessionId:guid}/complete")]
    public async Task<IActionResult> Complete(
        Guid sessionId,
        [FromBody] CompleteSessionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _lifecycleHandler.CompleteAsync(
            sessionId, request.EndShiftId, cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new { error = result.Error });
    }
}
