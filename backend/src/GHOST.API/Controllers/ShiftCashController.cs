using GHOST.API.Security;
using GHOST.Application.Abstractions.CurrentUser;
using GHOST.Application.Cash;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

/// <summary>
/// Cash drawer of a shift. Staff see and change only their own open shift; the actor is
/// always taken from the authenticated token, never from the request body.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.StaffOrAdmin)]
[Route("api/shifts/{shiftId:guid}/cash")]
public sealed class ShiftCashController : ControllerBase
{
    private readonly ShiftCashHandler _handler;
    private readonly ShiftAccess _shiftAccess;
    private readonly ICurrentUser _currentUser;

    public ShiftCashController(
        ShiftCashHandler handler,
        ShiftAccess shiftAccess,
        ICurrentUser currentUser)
    {
        _handler = handler;
        _shiftAccess = shiftAccess;
        _currentUser = currentUser;
    }

    public sealed record ExpenseRequest(
        decimal Amount,
        string? Category,
        string? Reason,
        string? IdempotencyKey);

    public sealed record WithdrawalRequest(
        decimal Amount,
        WithdrawalCategory Category,
        string? Reason,
        Guid? BeneficiaryUserId,
        string? IdempotencyKey);

    [HttpGet]
    public async Task<IActionResult> GetSummary(Guid shiftId, CancellationToken cancellationToken)
    {
        // Reading a closed shift's drawer is allowed for its owner, so use the close-level check.
        if (!await _shiftAccess.CanCloseShiftAsync(shiftId, cancellationToken))
            return Forbid();

        var result = await _handler.GetSummaryAsync(shiftId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { error = result.Error });
    }

    [HttpPost("expenses")]
    public async Task<IActionResult> RecordExpense(
        Guid shiftId,
        [FromBody] ExpenseRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Unauthorized();

        if (!await _shiftAccess.CanOperateOnShiftAsync(shiftId, cancellationToken))
            return Forbid();

        var result = await _handler.RecordExpenseAsync(
            new RecordExpenseCommand(
                shiftId,
                actorId.Value,
                request.Amount,
                request.Category ?? string.Empty,
                request.Reason ?? string.Empty,
                request.IdempotencyKey),
            cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : BadRequest(new { error = result.Error });
    }

    [HttpPost("withdrawals")]
    public async Task<IActionResult> RecordWithdrawal(
        Guid shiftId,
        [FromBody] WithdrawalRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
            return Unauthorized();

        if (!await _shiftAccess.CanOperateOnShiftAsync(shiftId, cancellationToken))
            return Forbid();

        var result = await _handler.RecordWithdrawalAsync(
            new RecordWithdrawalCommand(
                shiftId,
                actorId.Value,
                request.Amount,
                request.Category,
                request.Reason ?? string.Empty,
                request.BeneficiaryUserId,
                request.IdempotencyKey),
            cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : BadRequest(new { error = result.Error });
    }
}
