using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Common.Result;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Application.Devices.CreateDevice;

public sealed record DeviceRateInput(PlayType PlayType, decimal HourlyRate);

public sealed record CreateDeviceCommand(
    string Name,
    string Category,
    IReadOnlyCollection<DeviceRateInput> Rates);

public sealed class CreateDeviceHandler
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateDeviceHandler(
        IDeviceRepository deviceRepository,
        IUnitOfWork unitOfWork)
    {
        _deviceRepository = deviceRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> HandleAsync(
        CreateDeviceCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Rates is null || command.Rates.Count == 0)
        {
            return Result<Guid>.Failure(
                "At least one hourly rate must be configured for the device.");
        }

        if (command.Rates.Any(rate => !Enum.IsDefined(rate.PlayType)))
        {
            return Result<Guid>.Failure("A play type is invalid.");
        }

        if (command.Rates
            .GroupBy(rate => rate.PlayType)
            .Any(group => group.Count() > 1))
        {
            return Result<Guid>.Failure(
                "A play type can only have one rate per device.");
        }

        try
        {
            var device = new Device(command.Name, command.Category);

            foreach (var rate in command.Rates)
            {
                device.SetRate(rate.PlayType, rate.HourlyRate);
            }

            await _deviceRepository.AddAsync(device, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(device.Id);
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
