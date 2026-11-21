using System.Text.Json;
using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Common.Result;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Application.Sessions;

public sealed record CancelSessionCommand(
    Guid SessionId,
    Guid EndShiftId,
    Guid ActorUserId,
    bool ActorIsAdmin,
    SessionCancelReason Reason,
    string? Details);

public sealed record CancelledSessionSnapshot(
    Guid SessionId,
    string Status,
    bool IsVoided,
    decimal Amount,
    long BillableSeconds);

/// <summary>
/// SRS BD-03 / FR-25: free cancellation inside the window for any operator; afterwards only
/// an Admin can void, with a written reason. The device is released and the audit record is
/// committed in the same save as the cancellation.
/// </summary>
public sealed class CancelSessionHandler
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _auditTrail;
    private readonly SessionPolicy _policy;

    public CancelSessionHandler(
        ISessionRepository sessionRepository,
        IUnitOfWork unitOfWork,
        IAuditTrail auditTrail,
        SessionPolicy policy)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
        _auditTrail = auditTrail;
        _policy = policy;
    }

    public async Task<Result<CancelledSessionSnapshot>> HandleAsync(
        CancelSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.SessionId == Guid.Empty || command.EndShiftId == Guid.Empty ||
            command.ActorUserId == Guid.Empty)
        {
            return Result<CancelledSessionSnapshot>.Failure(
                "SessionId, EndShiftId and the acting user are required.");
        }

        var session = await _sessionRepository.GetByIdAsync(command.SessionId, cancellationToken);
        if (session is null)
            return Result<CancelledSessionSnapshot>.Failure("Session was not found.");

        var shift = await _sessionRepository.GetShiftAsync(command.EndShiftId, cancellationToken);
        if (shift is null)
            return Result<CancelledSessionSnapshot>.Failure("End shift was not found.");

        if (shift.Status != ShiftStatus.Open)
            return Result<CancelledSessionSnapshot>.Failure("A session can only be cancelled during an open shift.");

        var segment = session.GetActiveSegment();
        if (session.Status != SessionStatus.Active || segment is null)
            return Result<CancelledSessionSnapshot>.Failure("Session is not active.");

        var device = await _sessionRepository.GetDeviceForStartAsync(segment.DeviceId, cancellationToken);
        if (device is null)
            return Result<CancelledSessionSnapshot>.Failure("Session device was not found.");

        try
        {
            // Capture the played time before the session is closed; it is kept for analytics.
            var playedSeconds = (long)session.GetTotalBillableDuration().TotalSeconds;

            session.CancelOrVoid(
                command.EndShiftId,
                command.Reason,
                command.Details,
                _policy.CancelWindow,
                command.ActorIsAdmin);
            device.MarkAsAvailable();

            _auditTrail.Record(
                command.ActorUserId,
                session.IsVoided ? "Session.Voided" : "Session.Cancelled",
                "Session",
                session.Id.ToString(),
                JsonSerializer.Serialize(new
                {
                    reason = command.Reason.ToString(),
                    session.IsVoided,
                    deviceId = segment.DeviceId,
                    endShiftId = command.EndShiftId,
                    playedSeconds
                }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<CancelledSessionSnapshot>.Success(new CancelledSessionSnapshot(
                session.Id,
                session.Status.ToString(),
                session.IsVoided,
                session.GetTotalAmount(),
                playedSeconds));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<CancelledSessionSnapshot>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<CancelledSessionSnapshot>.Failure(exception.Message);
        }
    }
}
