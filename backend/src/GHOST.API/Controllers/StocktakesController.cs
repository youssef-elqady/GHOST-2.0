using GHOST.API.Security;
using GHOST.Application.Abstractions.CurrentUser;
using GHOST.Application.Inventory;
using GHOST.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

/// <summary>Blind stocktake. Staff count; only an Admin approves or rejects the adjustment.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.StaffOrAdmin)]
[Route("api/stocktakes")]
public sealed class StocktakesController : ControllerBase
{
    private readonly StocktakeHandler _handler;
    private readonly ICurrentUser _currentUser;

    public StocktakesController(StocktakeHandler handler, ICurrentUser currentUser)
    {
        _handler = handler;
        _currentUser = currentUser;
    }

    public sealed record CountRequest(Guid ProductId, decimal Quantity);

    public sealed record CountsRequest(List<CountRequest>? Counts);

    public sealed record RejectRequest(string? Reason);

    public sealed record ApproveRequest(string? Note);

    [HttpPost]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        var result = await _handler.StartAsync(actorId, _currentUser.IsAdmin, cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : BadRequest(new { error = result.Error });
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] StocktakeStatus? status, CancellationToken cancellationToken)
        => Ok(await _handler.ListAsync(status, _currentUser.IsAdmin, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _handler.GetAsync(id, _currentUser.IsAdmin, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { error = result.Error });
    }

    [HttpPut("{id:guid}/counts")]
    public async Task<IActionResult> Counts(Guid id, [FromBody] CountsRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        var counts = new Dictionary<Guid, decimal>();
        foreach (var count in request.Counts ?? new())
        {
            if (!counts.TryAdd(count.ProductId, count.Quantity))
                return BadRequest(new { error = "A product appears twice in the counts." });
        }

        var result = await _handler.RecordCountsAsync(id, actorId, _currentUser.IsAdmin, counts, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        var result = await _handler.SubmitAsync(id, actorId, _currentUser.IsAdmin, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveRequest? request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } adminId)
            return Unauthorized();

        var result = await _handler.ApproveAsync(id, adminId, request?.Note, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } adminId)
            return Unauthorized();

        var result = await _handler.RejectAsync(id, adminId, request.Reason ?? string.Empty, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }
}
