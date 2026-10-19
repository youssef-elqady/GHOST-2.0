using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Result;
using GHOST.Application.Common.Exceptions;
using GHOST.Domain.Entities;
using GHOST.Domain.Exceptions;
using GHOST.Domain.Enums;

namespace GHOST.Application.Sessions.StartSession;

public sealed class StartSessionHandler
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public StartSessionHandler(
        ISessionRepository sessionRepository,
        IUnitOfWork unitOfWork)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> HandleAsync(
        StartSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.ShiftId == Guid.Empty || command.DeviceId == Guid.Empty)
        {
            return Result<Guid>.Failure("ShiftId and DeviceId are required.");
        }

        if (!Enum.IsDefined(command.PlayType))
        {
            return Result<Guid>.Failure("PlayType is invalid.");
        }

        var shift = await _sessionRepository.GetShiftAsync(
            command.ShiftId,
            cancellationToken);

        if (shift is null)
        {
            return Result<Guid>.Failure("Shift was not found.");
        }

        if (shift.Status != ShiftStatus.Open)
        {
            return Result<Guid>.Failure("A session can only start during an open shift.");
        }

        var device = await _sessionRepository.GetDeviceForStartAsync(
            command.DeviceId,
            cancellationToken);

        if (device is null)
        {
            return Result<Guid>.Failure("Device was not found.");
        }

        if (!device.IsActive)
        {
            return Result<Guid>.Failure("An inactive device cannot start a session.");
        }

        var existingSession = await _sessionRepository.GetActiveByDeviceAsync(
            command.DeviceId,
            cancellationToken);

        if (existingSession is not null)
        {
            return Result<Guid>.Failure("This device already has an active session.");
        }

        try
        {
            var appliedHourlyRate = device.GetRate(command.PlayType);

            // Domain validation controls whether this device may start.
            device.MarkAsRunning(command.PlayType);

            var session = new Session(
                command.ShiftId,
                command.DeviceId,
                command.PlayType,
                appliedHourlyRate);

            await _sessionRepository.AddAsync(session, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(session.Id);
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
