using GHOST.Application.BusinessDays.OpenShift;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

[ApiController]
[Route("api/business-days")]
public sealed class BusinessDaysController : ControllerBase
{
    private readonly OpenShiftHandler _openShiftHandler;

    public BusinessDaysController(OpenShiftHandler openShiftHandler)
    {
        _openShiftHandler = openShiftHandler;
    }

    public sealed record OpenShiftRequest(Guid StaffUserId, decimal OpeningCash);

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
