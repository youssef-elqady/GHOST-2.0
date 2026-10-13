
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Result;
using GHOST.Domain.Entities;

namespace GHOST.Application.BusinessDays.OpenBusinessDay;

public sealed class OpenBusinessDayHandler
{
    private readonly IBusinessDayRepository _businessDayRepository;
    private readonly IUnitOfWork _unitOfWork;

    public OpenBusinessDayHandler(
        IBusinessDayRepository businessDayRepository,
        IUnitOfWork unitOfWork)
    {
        _businessDayRepository = businessDayRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> HandleAsync(
        OpenBusinessDayCommand command,
        CancellationToken cancellationToken = default)
    {
        var existingOpenBusinessDay =
            await _businessDayRepository.GetOpenAsync(
                cancellationToken);

        if (existingOpenBusinessDay is not null)
        {
            return Result<Guid>.Failure(
                $"There is already an open business day " +
                $"({existingOpenBusinessDay.BusinessDate}).");
        }

        var existingBusinessDay =
            await _businessDayRepository.GetByDateAsync(
                command.BusinessDate,
                cancellationToken);

        if (existingBusinessDay is not null)
        {
            return Result<Guid>.Failure(
                $"A business day already exists for " +
                $"{command.BusinessDate}.");
        }

        var businessDay = new BusinessDay(command.BusinessDate);

        await _businessDayRepository.AddAsync(
            businessDay,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Result<Guid>.Success(businessDay.Id);
    }
}