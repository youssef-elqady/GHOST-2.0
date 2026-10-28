using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;
using GHOST.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GHOST.API.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public ProductsController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    public sealed record CreateProductRequest(
        string Name,
        string? Sku,
        string Category,
        string Unit,
        decimal SellingPrice,
        decimal MinimumStock);

    public sealed record UpdateProductRequest(
        string Name,
        string? Sku,
        string Category,
        string Unit,
        decimal SellingPrice,
        decimal MinimumStock,
        bool IsActive);

    public sealed record ReceiveStockRequest(
        decimal Quantity,
        decimal UnitCost,
        string? Reference,
        string? Notes);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var products = await _inventoryService.GetProductsAsync(cancellationToken);
        return Ok(products.Select(ToProductResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var product = await _inventoryService.GetProductAsync(id, cancellationToken);
        return product is null ? NotFound(new { error = "Product not found." }) : Ok(ToProductResponse(product));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var product = new Product(
                request.Name,
                request.Sku,
                request.Category,
                request.Unit,
                request.SellingPrice,
                request.MinimumStock);

            var created = await _inventoryService.CreateProductAsync(product, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToProductResponse(created));
        }
        catch (BusinessRuleValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "The product could not be saved. Check that its SKU is unique and the database constraints are satisfied." });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _inventoryService.UpdateProductAsync(
                id,
                request.Name,
                request.Sku,
                request.Category,
                request.Unit,
                request.SellingPrice,
                request.MinimumStock,
                request.IsActive,
                cancellationToken);

            return updated is null
                ? NotFound(new { error = "Product not found." })
                : Ok(ToProductResponse(updated));
        }
        catch (BusinessRuleValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The product changed in another operation. Refresh and try again." });
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "The product could not be saved. Check that its SKU is unique and the database constraints are satisfied." });
        }
    }

    [HttpPost("{id:guid}/stock/receive")]
    public async Task<IActionResult> ReceiveStock(
        Guid id,
        [FromBody] ReceiveStockRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var movement = await _inventoryService.ReceiveStockAsync(
                id,
                request.Quantity,
                request.UnitCost,
                request.Reference,
                request.Notes,
                cancellationToken);

            return movement is null
                ? NotFound(new { error = "Product not found." })
                : Ok(ToMovementResponse(movement));
        }
        catch (BusinessRuleValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "Inventory changed in another operation. Refresh the product and retry." });
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "The inventory receipt could not be saved. No partial receipt was committed." });
        }
    }

    [HttpGet("{id:guid}/stock-movements")]
    public async Task<IActionResult> GetStockMovements(
        Guid id,
        CancellationToken cancellationToken)
    {
        var product = await _inventoryService.GetProductAsync(id, cancellationToken);
        if (product is null)
            return NotFound(new { error = "Product not found." });

        var movements = await _inventoryService.GetStockMovementsAsync(id, cancellationToken);
        return Ok(movements.Select(ToMovementResponse));
    }

    private static object ToProductResponse(Product product) => new
    {
        product.Id,
        product.Name,
        product.Sku,
        product.Category,
        product.Unit,
        product.SellingPrice,
        product.AverageUnitCost,
        product.StockQuantity,
        product.MinimumStock,
        product.IsLowStock,
        product.IsActive,
        product.CreatedAt,
        product.UpdatedAt
    };

    private static object ToMovementResponse(StockMovement movement) => new
    {
        movement.Id,
        movement.ProductId,
        Type = movement.Type.ToString(),
        movement.QuantityDelta,
        movement.UnitCost,
        movement.BalanceAfter,
        movement.Reference,
        movement.Notes,
        movement.CreatedAt
    };
}
