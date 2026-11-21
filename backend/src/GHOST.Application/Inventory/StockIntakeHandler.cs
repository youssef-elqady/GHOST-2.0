using System.Text.Json;
using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Common.Result;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Application.Inventory;

public sealed record IntakeMovementInfo(Guid Id, Guid ProductId, StockMovementType Type, decimal QtyDelta, Guid UserId);

public sealed record IntakeReportRow(Guid UserId, string? UserName, Guid ProductId, string ProductName, int Operations, decimal Quantity, decimal Cost);

/// <summary>Read and lookup side for stock intake, kept separate from IProductRepository.</summary>
public interface IStockIntakeStore
{
    Task<IntakeMovementInfo?> GetMovementAsync(Guid movementId, CancellationToken cancellationToken = default);

    /// <summary>Total quantity already removed by corrections of this movement.</summary>
    Task<decimal> GetCorrectedQuantityAsync(Guid movementId, CancellationToken cancellationToken = default);

    /// <summary>Intake (Purchase) and correction totals per employee and product for the period.</summary>
    Task<IReadOnlyList<IntakeReportRow>> GetIntakeReportAsync(
        DateTimeOffset rangeFrom, DateTimeOffset rangeTo, CancellationToken cancellationToken = default);
}

public sealed record CorrectIntakeCommand(Guid MovementId, Guid ActorUserId, bool ActorIsAdmin, decimal Quantity, string? Reason, Guid? ShiftId);

/// <summary>
/// Corrects a mistaken intake quantity without touching the original movement. The correction is a new movement,
/// linked to the original, with a mandatory reason. Only the person who entered the intake, or an Admin, may correct it.
/// A correction only removes stock (an under-entered quantity is fixed with a new intake). It does not move cash.
/// </summary>
public sealed class StockIntakeHandler
{
    private readonly IProductRepository _products;
    private readonly IStockIntakeStore _store;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _audit;
    private readonly IRejectionAudit? _rejections;

    public StockIntakeHandler(IProductRepository products, IStockIntakeStore store, IUnitOfWork unitOfWork, IAuditTrail audit, IRejectionAudit? rejections = null)
    {
        _rejections = rejections;
        _products = products;
        _store = store;
        _unitOfWork = unitOfWork;
        _audit = audit;
    }

    public async Task<Result<StockMovementDto>> CorrectAsync(CorrectIntakeCommand command, CancellationToken cancellationToken = default)
    {
        var result = await CorrectCoreAsync(command, cancellationToken);
        if (!result.IsSuccess)
            await _rejections.RejectedAsync(command.ActorUserId, "Stock.IntakeCorrectionRejected", "StockMovement", command.MovementId, result.Error, cancellationToken);
        return result;
    }

    private async Task<Result<StockMovementDto>> CorrectCoreAsync(CorrectIntakeCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            return Result<StockMovementDto>.Failure("A correction reason is required.");

        var original = await _store.GetMovementAsync(command.MovementId, cancellationToken);
        if (original is null)
            return Result<StockMovementDto>.Failure("The original intake was not found.");

        if (original.Type != StockMovementType.Purchase || original.QtyDelta <= 0)
            return Result<StockMovementDto>.Failure("Only a stock intake can be corrected here.");

        if (!command.ActorIsAdmin && original.UserId != command.ActorUserId)
            return Result<StockMovementDto>.Failure("You can only correct intakes you entered.");

        if (command.Quantity <= 0)
            return Result<StockMovementDto>.Failure("Quantity must be greater than zero.");

        var already = await _store.GetCorrectedQuantityAsync(original.Id, cancellationToken);
        if (already + command.Quantity > original.QtyDelta)
            return Result<StockMovementDto>.Failure(
                $"A correction cannot exceed the original quantity. Original {original.QtyDelta:N3}, already corrected {already:N3}.");

        var product = await _products.GetAsync(original.ProductId, cancellationToken);
        if (product is null)
            return Result<StockMovementDto>.Failure("Product was not found.");

        try
        {
            var movement = product.ApplyMovement(
                StockMovementType.IntakeCorrection, command.Quantity, command.ActorUserId, command.Reason,
                command.ShiftId, null, "StockMovement", original.Id);

            _audit.Record(command.ActorUserId, "Stock.IntakeCorrected", "Product", product.Id.ToString(),
                JsonSerializer.Serialize(new { originalMovementId = original.Id, quantity = command.Quantity, command.Reason }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<StockMovementDto>.Success(new StockMovementDto(
                movement.Id, movement.Type.ToString(), movement.QtyDelta, movement.UnitCost, movement.BalanceAfter,
                movement.ShiftId, movement.UserId, movement.Reason, movement.CreatedAt));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<StockMovementDto>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<StockMovementDto>.Failure(exception.Message);
        }
    }

    public Task<IReadOnlyList<IntakeReportRow>> ReportAsync(DateTimeOffset rangeFrom, DateTimeOffset rangeTo, CancellationToken cancellationToken = default)
        => _store.GetIntakeReportAsync(rangeFrom, rangeTo, cancellationToken);
}
