using GHOST.API.Contracts.BusinessDays;
using GHOST.Application.BusinessDays.CloseShift;
using GHOST.Application.BusinessDays.OpenBusinessDay;
using GHOST.Application.BusinessDays.OpenShift;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

[ApiController]
[Route("api/business-days")]
public sealed class BusinessDaysController : ControllerBase
{
    private readonly OpenBusinessDayHandler _openBusinessDayHandler;
    private readonly OpenShiftHandler _openShiftHandler;
    private readonly CloseShiftHandler _closeShiftHandler;

    public BusinessDaysController(
        OpenBusinessDayHandler openBusinessDayHandler,
        OpenShiftHandler openShiftHandler,
        CloseShiftHandler closeShiftHandler)
    {
        _openBusinessDayHandler = openBusinessDayHandler;
        _openShiftHandler = openShiftHandler;
        _closeShiftHandler = closeShiftHandler;
    }

    public sealed record OpenShiftRequest(Guid StaffUserId, decimal OpeningCash);
    public sealed record CloseShiftRequest(decimal ActualCash, string? ClosingReason);

    [HttpPost]
    public async Task<IActionResult> OpenBusinessDay(
        [FromBody] OpenBusinessDayRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _openBusinessDayHandler.HandleAsync(
            new OpenBusinessDayCommand(request.BusinessDate),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return Conflict(new { error = result.Error });
        }

        return StatusCode(
            StatusCodes.Status201Created,
            new
            {
                businessDayId = result.Value,
                businessDate = request.BusinessDate
            });
    }

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
        {
            return BadRequest(new { error = result.Error });
        }

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
        {
            return BadRequest(new { error = result.Error });
        }

        return StatusCode(
            StatusCodes.Status201Created,
            new { shiftId = result.Value });
    }
}
