using GHOST.API.Security;
using GHOST.Application.Abstractions.CurrentUser;
using GHOST.Application.Inventory;
using GHOST.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

/// <summary>
/// Product catalog and stock. Staff can read, quick-add (price pending) and record waste or
/// manual adjustments; Admin prices, enables and records any movement. The actor always comes
/// from the token.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.StaffOrAdmin)]
[Route("api/products")]
public sealed class ProductsController : ControllerBase
{
    private readonly InventoryHandler _handler;
    private readonly StocktakeHandler _stocktakeHandler;
    private readonly ICurrentUser _currentUser;

    public ProductsController(InventoryHandler handler, StocktakeHandler stocktakeHandler, ICurrentUser currentUser)
    {
        _handler = handler;
        _stocktakeHandler = stocktakeHandler;
        _currentUser = currentUser;
    }

    public sealed record CreateProductRequest(
        string Name,
        string? Barcode,
        string? Sku,
        string Category,
        string Unit,
        decimal? SalePrice,
        decimal? MinStock,
        decimal? WarningStock,
        bool? TrackStock);

    public sealed record SetPriceRequest(decimal Price, string? Reason);

    public sealed record AdjustStockRequest(
        StockMovementType Type,
        decimal Quantity,
        decimal? UnitCost,
        Guid? ShiftId,
        string? Reason);

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] ProductStatus? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
        => Ok(await _handler.ListAsync(status, search, cancellationToken));

    [HttpGet("by-barcode/{barcode}")]
    public async Task<IActionResult> GetByBarcode(string barcode, CancellationToken cancellationToken)
    {
        var result = await _handler.GetByBarcodeAsync(barcode, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { error = result.Error });
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        var result = await _handler.CreateProductAsync(
            new CreateProductCommand(
                actorId,
                _currentUser.IsAdmin,
                request.Name,
                request.Barcode,
                request.Sku,
                request.Category,
                request.Unit,
                request.SalePrice,
                request.MinStock,
                request.WarningStock,
                request.TrackStock ?? true),
            cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : BadRequest(new { error = result.Error });
    }

    [HttpPut("{id:guid}/price")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> SetPrice(
        Guid id,
        [FromBody] SetPriceRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        var result = await _handler.SetPriceAsync(id, actorId, request.Price, request.Reason, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/enable")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public Task<IActionResult> Enable(Guid id, CancellationToken cancellationToken)
        => SetStatus(id, true, cancellationToken);

    [HttpPost("{id:guid}/disable")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public Task<IActionResult> Disable(Guid id, CancellationToken cancellationToken)
        => SetStatus(id, false, cancellationToken);

    [HttpGet("{id:guid}/movements")]
    public async Task<IActionResult> Movements(
        Guid id,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
        => Ok(await _handler.GetMovementsAsync(id, take, cancellationToken));

    [HttpPost("{id:guid}/movements")]
    public async Task<IActionResult> AdjustStock(
        Guid id,
        [FromBody] AdjustStockRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        var result = await _handler.AdjustStockAsync(
            new AdjustStockCommand(id, actorId, request.ShiftId, request.Type, request.Quantity,
                request.UnitCost, request.Reason),
            _currentUser.IsAdmin,
            cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : BadRequest(new { error = result.Error });
    }

    /// <summary>Gives a product without a manufacturer barcode its own internal EAN-13.</summary>
    [HttpPost("{id:guid}/barcode")]
    public async Task<IActionResult> GenerateBarcode(Guid id, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        var result = await _stocktakeHandler.GenerateBarcodeAsync(id, actorId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    private async Task<IActionResult> SetStatus(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        var result = await _handler.SetStatusAsync(id, actorId, enabled, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }
}
