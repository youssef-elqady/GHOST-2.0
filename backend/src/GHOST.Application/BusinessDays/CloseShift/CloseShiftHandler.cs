using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Common.Result;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Application.BusinessDays.CloseShift;

public sealed record CloseShiftCommand(
    Guid BusinessDayId,
    Guid ShiftId,
    decimal ActualCash,
    string? ClosingReason);

public sealed record ClosedShiftSnapshot(
    Guid ShiftId,
    string Status,
    decimal ExpectedCash,
    decimal ActualCash,
    decimal CashDifference,
    string? ClosingReason);

public sealed class CloseShiftHandler
{
    private readonly IBusinessDayRepository _businessDayRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CloseShiftHandler(
        IBusinessDayRepository businessDayRepository,
        IUnitOfWork unitOfWork)
    {
        _businessDayRepository = businessDayRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ClosedShiftSnapshot>> HandleAsync(
        CloseShiftCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.BusinessDayId == Guid.Empty || command.ShiftId == Guid.Empty)
            return Result<ClosedShiftSnapshot>.Failure("BusinessDayId and ShiftId are required.");

        var businessDay = await _businessDayRepository.GetForShiftOpeningAsync(
            command.BusinessDayId, cancellationToken);

        if (businessDay is null)
            return Result<ClosedShiftSnapshot>.Failure("Business day was not found.");

        var shift = businessDay.Shifts.FirstOrDefault(item => item.Id == command.ShiftId);
        if (shift is null)
            return Result<ClosedShiftSnapshot>.Failure("Shift was not found in this business day.");

        try
        {
            shift.Close(command.ActualCash, command.ClosingReason);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<ClosedShiftSnapshot>.Success(new ClosedShiftSnapshot(
                shift.Id,
                shift.Status.ToString(),
                shift.ExpectedCash,
                shift.ActualCash!.Value,
                shift.CashDifference!.Value,
                shift.ClosingReason));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<ClosedShiftSnapshot>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<ClosedShiftSnapshot>.Failure(exception.Message);
        }
    }
}
