using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Common.Result;
using GHOST.Domain.Exceptions;

namespace GHOST.Application.BusinessDays.OpenShift;

public sealed record OpenShiftCommand(
    Guid BusinessDayId,
    Guid StaffUserId,
    decimal OpeningCash);

public sealed class OpenShiftHandler
{
    private readonly IBusinessDayRepository _businessDayRepository;
    private readonly IUnitOfWork _unitOfWork;

    public OpenShiftHandler(
        IBusinessDayRepository businessDayRepository,
        IUnitOfWork unitOfWork)
    {
        _businessDayRepository = businessDayRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> HandleAsync(
        OpenShiftCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.BusinessDayId == Guid.Empty || command.StaffUserId == Guid.Empty)
            return Result<Guid>.Failure("BusinessDayId and StaffUserId are required.");

        var businessDay = await _businessDayRepository.GetForShiftOpeningAsync(
            command.BusinessDayId, cancellationToken);

        if (businessDay is null)
            return Result<Guid>.Failure("Business day was not found.");

        try
        {
            var shift = businessDay.OpenShift(command.StaffUserId, command.OpeningCash);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<Guid>.Success(shift.Id);
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<Guid>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<Guid>.Failure(exception.Message);
        }
    }
}
