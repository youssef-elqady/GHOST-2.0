using GHOST.API.Security;
using GHOST.Application.Abstractions.CurrentUser;
using GHOST.Application.Sales;
using GHOST.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

/// <summary>
/// Point of sale. Staff sell on their own open shift; discounts and refunds are Admin only.
/// The acting user always comes from the token.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.StaffOrAdmin)]
[Route("api/sales")]
public sealed class SalesController : ControllerBase
{
    private readonly SaleHandler _handler;
    private readonly ShiftAccess _shiftAccess;
    private readonly ICurrentUser _currentUser;

    public SalesController(SaleHandler handler, ShiftAccess shiftAccess, ICurrentUser currentUser)
    {
        _handler = handler;
        _shiftAccess = shiftAccess;
        _currentUser = currentUser;
    }

    public sealed record CheckoutLine(Guid ProductId, decimal Quantity);

    public sealed record CheckoutDiscount(DiscountType Type, decimal Value, string? Reason);

    public sealed record CheckoutRequest(
        Guid ShiftId,
        List<CheckoutLine>? Items,
        Guid? SessionId,
        CheckoutDiscount? Discount,
        string? IdempotencyKey,
        string? RebillReason = null);

    public sealed record RefundRequest(string? Reason, bool ReturnToStock, Guid CashShiftId);

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        if (!await _shiftAccess.CanOperateOnShiftAsync(request.ShiftId, cancellationToken))
            return Forbid();

        var result = await _handler.CheckoutCashAsync(
            new CheckoutCommand(
                request.ShiftId,
                actorId,
                _currentUser.IsAdmin,
                (request.Items ?? new()).Select(i => new SaleLineInput(i.ProductId, i.Quantity)).ToList(),
                request.SessionId,
                request.Discount is null
                    ? null
                    : new DiscountRequest(request.Discount.Type, request.Discount.Value, request.Discount.Reason),
                request.IdempotencyKey,
                request.RebillReason),
            cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : BadRequest(new { error = result.Error });
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? shiftId,
        [FromQuery] SaleStatus? status,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        // Staff see only invoices they created.
        return Ok(await _handler.ListAsync(shiftId, status, _currentUser.IsAdmin ? null : actorId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _handler.GetAsync(id, cancellationToken);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });

        if (!_currentUser.IsAdmin && result.Value!.CreatedById != _currentUser.UserId)
            return Forbid();

        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/print")]
    public async Task<IActionResult> Print(Guid id, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        if (!_currentUser.IsAdmin)
        {
            var sale = await _handler.GetAsync(id, cancellationToken);
            if (!sale.IsSuccess)
                return NotFound(new { error = sale.Error });
            if (sale.Value!.CreatedById != actorId)
                return Forbid();
        }

        var result = await _handler.PrintAsync(id, actorId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/refund")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Refund(Guid id, [FromBody] RefundRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } adminId)
            return Unauthorized();

        var result = await _handler.RefundAsync(
            id, adminId, request.Reason ?? string.Empty, request.ReturnToStock, request.CashShiftId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }
}
