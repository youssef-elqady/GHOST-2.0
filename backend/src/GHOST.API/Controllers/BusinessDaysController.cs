using GHOST.API.Contracts.BusinessDays;
using GHOST.Application.BusinessDays.CloseShift;
using GHOST.Application.BusinessDays.OpenBusinessDay;
using GHOST.Application.BusinessDays.OpenShift;
using GHOST.API.Security;
using GHOST.Application.Abstractions.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/business-days")]
public sealed class BusinessDaysController : ControllerBase
{
    private readonly OpenBusinessDayHandler _openBusinessDayHandler;
    private readonly OpenShiftHandler _openShiftHandler;
    private readonly CloseShiftHandler _closeShiftHandler;
    private readonly ICurrentUser _currentUser;
    private readonly ShiftAccess _shiftAccess;

    public BusinessDaysController(
        OpenBusinessDayHandler openBusinessDayHandler,
        OpenShiftHandler openShiftHandler,
        CloseShiftHandler closeShiftHandler,
        ICurrentUser currentUser,
        ShiftAccess shiftAccess)
    {
        _currentUser = currentUser;
        _shiftAccess = shiftAccess;
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

    [Authorize(Policy = AuthorizationPolicies.StaffOrAdmin)]
    [HttpPost("{businessDayId:guid}/shifts/{shiftId:guid}/close")]
    public async Task<IActionResult> CloseShift(
        Guid businessDayId,
        Guid shiftId,
        [FromBody] CloseShiftRequest request,
        CancellationToken cancellationToken)
    {
        if (!await _shiftAccess.CanCloseShiftAsync(shiftId, cancellationToken))
            return Forbid();

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

    [Authorize(Policy = AuthorizationPolicies.StaffOrAdmin)]
    [HttpPost("{businessDayId:guid}/shifts")]
    public async Task<IActionResult> OpenShift(
        Guid businessDayId,
        [FromBody] OpenShiftRequest request,
        CancellationToken cancellationToken)
    {
        // Staff can only open a shift for themselves; only Admin may open one for someone else.
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Unauthorized();

        if (!_currentUser.IsAdmin && request.StaffUserId != actorId.Value)
            return Forbid();

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
