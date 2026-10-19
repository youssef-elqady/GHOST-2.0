using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Common.Result;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Application.Sessions;

public sealed record TransferSessionCommand(
    Guid SessionId,
    Guid NewDeviceId,
    PlayType PlayType);

public sealed class TransferSessionHandler
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public TransferSessionHandler(
        ISessionRepository sessionRepository,
        IUnitOfWork unitOfWork)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SessionSnapshot>> HandleAsync(
        TransferSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.SessionId == Guid.Empty || command.NewDeviceId == Guid.Empty)
            return Result<SessionSnapshot>.Failure("SessionId and NewDeviceId are required.");

        var session = await _sessionRepository.GetByIdAsync(
            command.SessionId, cancellationToken);

        if (session is null)
            return Result<SessionSnapshot>.Failure("Session was not found.");

        if (session.Status != SessionStatus.Active)
            return Result<SessionSnapshot>.Failure("Only an active session can be transferred.");

        var currentSegment = session.GetActiveSegment();
        if (currentSegment is null)
            return Result<SessionSnapshot>.Failure("Session has no active device segment.");

        if (currentSegment.Pauses.Any(pause => pause.IsActive))
            return Result<SessionSnapshot>.Failure(
                "Resume the session before transferring it to another device.");

        if (currentSegment.DeviceId == command.NewDeviceId)
            return Result<SessionSnapshot>.Failure("Cannot transfer to the same device.");

        var currentDevice = await _sessionRepository.GetDeviceForStartAsync(
            currentSegment.DeviceId, cancellationToken);
        if (currentDevice is null)
            return Result<SessionSnapshot>.Failure("Current device was not found.");

        var destination = await _sessionRepository.GetDeviceForStartAsync(
            command.NewDeviceId, cancellationToken);
        if (destination is null)
            return Result<SessionSnapshot>.Failure("Destination device was not found.");

        if (destination.Status != DeviceStatus.Available)
            return Result<SessionSnapshot>.Failure(
                "Destination device must be available before transfer.");

        try
        {
            var appliedRate = destination.GetRate(command.PlayType);

            // Apply all three domain changes in one unit of work. The database
            // uniqueness constraints protect against concurrent transfers/starts.
            session.TransferToDevice(destination.Id, command.PlayType, appliedRate);
            currentDevice.MarkAsAvailable();
            destination.MarkAsRunning(command.PlayType);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<SessionSnapshot>.Success(new SessionSnapshot(
                session.Id,
                destination.Id,
                "Running",
                session.GetTotalAmount(),
                (long)session.GetTotalBillableDuration().TotalSeconds));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<SessionSnapshot>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<SessionSnapshot>.Failure(exception.Message);
        }
    }
}
