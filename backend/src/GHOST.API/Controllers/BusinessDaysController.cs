using GHOST.Application.BusinessDays.OpenShift;
using GHOST.Application.BusinessDays.CloseShift;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

[ApiController]
[Route("api/business-days")]
public sealed class BusinessDaysController : ControllerBase
{
    private readonly OpenShiftHandler _openShiftHandler;
    private readonly CloseShiftHandler _closeShiftHandler;

    public BusinessDaysController(
        OpenShiftHandler openShiftHandler,
        CloseShiftHandler closeShiftHandler)
    {
        _openShiftHandler = openShiftHandler;
        _closeShiftHandler = closeShiftHandler;
    }

    public sealed record OpenShiftRequest(Guid StaffUserId, decimal OpeningCash);
    public sealed record CloseShiftRequest(decimal ActualCash, string? ClosingReason);

    [HttpPost("{businessDayId:guid}/shifts/{shiftId:guid}/close")]
    public async Task<IActionResult> CloseShift(
        Guid businessDayId,
        Guid shiftId,
        [FromBody] CloseShiftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _closeShiftHandler.HandleAsync(
            new CloseShiftCommand(
                businessDayId,
                shiftId,
                request.ActualCash,
                request.ClosingReason),
            cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Value);
    }

    [HttpPost("{businessDayId:guid}/shifts")]
    public async Task<IActionResult> OpenShift(
        Guid businessDayId,
        [FromBody] OpenShiftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _openShiftHandler.HandleAsync(
            new OpenShiftCommand(
                businessDayId,
                request.StaffUserId,
                request.OpeningCash),
            cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return StatusCode(
            StatusCodes.Status201Created,
            new { shiftId = result.Value });
    }
}
