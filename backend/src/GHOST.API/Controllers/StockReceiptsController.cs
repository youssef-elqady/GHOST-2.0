using GHOST.API.Security;
using GHOST.Application.Abstractions.CurrentUser;
using GHOST.Application.Inventory;
using GHOST.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

/// <summary>Staff record received goods on their own open shift; Admin approves or rejects.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.StaffOrAdmin)]
[Route("api/stock-receipts")]
public sealed class StockReceiptsController : ControllerBase
{
    private readonly StockReceiptHandler _handler;
    private readonly ShiftAccess _shiftAccess;
    private readonly ICurrentUser _currentUser;

    public StockReceiptsController(StockReceiptHandler handler, ShiftAccess shiftAccess, ICurrentUser currentUser)
    {
        _handler = handler;
        _shiftAccess = shiftAccess;
        _currentUser = currentUser;
    }

    public sealed record ReceiptLineRequest(Guid ProductId, decimal Quantity, decimal UnitCost, DateOnly? ExpiryDate);

    public sealed record CreateReceiptRequest(Guid ShiftId, string? SupplierName, List<ReceiptLineRequest>? Items);

    public sealed record RejectReceiptRequest(string? Reason, ReceiptRejectionOutcome Outcome, Guid? CashReturnShiftId);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReceiptRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        if (!await _shiftAccess.CanOperateOnShiftAsync(request.ShiftId, cancellationToken))
            return Forbid();

        var result = await _handler.CreateAsync(
            new CreateReceiptCommand(
                request.ShiftId,
                actorId,
                request.SupplierName,
                (request.Items ?? new()).Select(i => new ReceiptLineInput(i.ProductId, i.Quantity, i.UnitCost, i.ExpiryDate)).ToList()),
            cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : BadRequest(new { error = result.Error });
    }

    /// <summary>Staff see only their own receipts; Admin sees all and can filter by status.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] StockReceiptStatus? status, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        return Ok(await _handler.ListAsync(status, _currentUser.IsAdmin ? null : actorId, cancellationToken));
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } adminId)
            return Unauthorized();

        var result = await _handler.ApproveAsync(id, adminId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectReceiptRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } adminId)
            return Unauthorized();

        var result = await _handler.RejectAsync(
            new RejectReceiptCommand(id, adminId, request.Reason ?? string.Empty, request.Outcome, request.CashReturnShiftId),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }
}
