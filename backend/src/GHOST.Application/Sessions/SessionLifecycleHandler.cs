using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Result;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Application.Sessions;

public sealed record SessionSnapshot(
    Guid SessionId,
    Guid DeviceId,
    string Status,
    decimal Amount,
    long BillableSeconds);

public sealed class SessionLifecycleHandler
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SessionLifecycleHandler(
        ISessionRepository sessionRepository,
        IUnitOfWork unitOfWork)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
    }

    public Task<Result<SessionSnapshot>> PauseAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
        => ChangeActiveSessionAsync(sessionId, pause: true, cancellationToken);

    public Task<Result<SessionSnapshot>> ResumeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
        => ChangeActiveSessionAsync(sessionId, pause: false, cancellationToken);

    private async Task<Result<SessionSnapshot>> ChangeActiveSessionAsync(
        Guid sessionId,
        bool pause,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
            return Result<SessionSnapshot>.Failure("SessionId is required.");

        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
            return Result<SessionSnapshot>.Failure("Session was not found.");

        var segment = session.GetActiveSegment();
        if (session.Status != SessionStatus.Active || segment is null)
            return Result<SessionSnapshot>.Failure("Session has no active segment.");

        var device = await _sessionRepository.GetDeviceForStartAsync(
            segment.DeviceId, cancellationToken);
        if (device is null)
            return Result<SessionSnapshot>.Failure("Session device was not found.");

        try
        {
            if (pause)
            {
                session.PauseActiveDevice();
                device.MarkAsPaused();
            }
            else
            {
                session.ResumeActiveDevice();
                device.MarkAsResumed();
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<SessionSnapshot>.Success(CreateSnapshot(session));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<SessionSnapshot>.Failure(exception.Message);
        }
    }

    public async Task<Result<SessionSnapshot>> CompleteAsync(
        Guid sessionId,
        Guid endShiftId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty || endShiftId == Guid.Empty)
            return Result<SessionSnapshot>.Failure("SessionId and EndShiftId are required.");

        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
            return Result<SessionSnapshot>.Failure("Session was not found.");

        var shift = await _sessionRepository.GetShiftAsync(endShiftId, cancellationToken);
        if (shift is null)
            return Result<SessionSnapshot>.Failure("End shift was not found.");

        if (shift.Status != ShiftStatus.Open)
            return Result<SessionSnapshot>.Failure("Session can only end during an open shift.");

        var segment = session.GetActiveSegment();
        if (session.Status != SessionStatus.Active || segment is null)
            return Result<SessionSnapshot>.Failure("Session is not active.");

        var device = await _sessionRepository.GetDeviceForStartAsync(
            segment.DeviceId, cancellationToken);
        if (device is null)
            return Result<SessionSnapshot>.Failure("Session device was not found.");

        try
        {
            session.CompleteSession(endShiftId);
            device.MarkAsAvailable();

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<SessionSnapshot>.Success(CreateSnapshot(session));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<SessionSnapshot>.Failure(exception.Message);
        }
    }

    private static SessionSnapshot CreateSnapshot(Session session)
    {
        var segment = session.GetActiveSegment() ?? session.Segments
            .OrderByDescending(item => item.StartedAt)
            .First();

        return new SessionSnapshot(
            session.Id,
            segment.DeviceId,
            session.Status == SessionStatus.Active
                ? (segment.Pauses.Any(pause => pause.IsActive) ? "Paused" : "Running")
                : session.Status.ToString(),
            session.GetTotalAmount(),
            (long)session.GetTotalBillableDuration().TotalSeconds);
    }
}
