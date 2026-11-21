using System.Text.Json;
using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Common.Result;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Application.Sessions;

public sealed record OutageReviewItem(
    Guid SessionId,
    Guid? DeviceId,
    string? DeviceName,
    DateTimeOffset StartedAt,
    DateTimeOffset OutageStartedAt,
    DateTimeOffset OutageEndedAt,
    decimal AmountIfEndedAtOutageStart,
    long BillableSecondsSoFar);

public sealed record ApproveOutageCommand(
    Guid SessionId,
    Guid EndShiftId,
    DateTimeOffset ActualEndAt,
    Guid AdminUserId,
    string? Note);

public sealed record OutageApprovalResult(Guid SessionId, decimal TotalAmount, long BillableSeconds);

public sealed record OutageRecoveryResult(bool OutageDetected, TimeSpan Gap, int SessionsFlagged);

/// <summary>
/// Power cut / crash handling. While running, the host writes a heartbeat. On start-up, a gap longer than the
/// threshold means the PC was off: every active session keeps its last saved state, the outage is NOT billed
/// (stored as a closed pause), and the session waits for an Admin to set the real end time. All steps are audited.
/// </summary>
public sealed class PowerOutageHandler
{
    private readonly IOutageRepository _outages;
    private readonly ISessionRepository _sessions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _audit;
    private readonly ISystemAuditTrail _systemAudit;

    private readonly IRejectionAudit? _rejections;

    public PowerOutageHandler(
        IOutageRepository outages,
        ISessionRepository sessions,
        IUnitOfWork unitOfWork,
        IAuditTrail audit,
        ISystemAuditTrail systemAudit,
        IRejectionAudit? rejections = null)
    {
        _rejections = rejections;
        _outages = outages;
        _sessions = sessions;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _systemAudit = systemAudit;
    }

    public async Task<OutageRecoveryResult> RecoverAsync(
        DateTimeOffset now, TimeSpan threshold, CancellationToken cancellationToken = default)
    {
        var lastBeat = await _outages.GetHeartbeatAsync(cancellationToken);
        if (lastBeat is null || now - lastBeat.Value < threshold)
        {
            await _outages.SaveHeartbeatAsync(now, cancellationToken);
            return new OutageRecoveryResult(false, TimeSpan.Zero, 0);
        }

        var gap = now - lastBeat.Value;
        var active = await _outages.ListActiveSessionsAsync(cancellationToken);
        var flagged = 0;
        foreach (var session in active)
        {
            if (session.GetActiveSegment() is null)
                continue;

            session.MarkPowerOutage(lastBeat.Value, now);
            _systemAudit.Record(
                "Session.PowerOutageDetected",
                "Session",
                session.Id.ToString(),
                JsonSerializer.Serialize(new
                {
                    lastSavedAt = lastBeat.Value,
                    recoveredAt = now,
                    gapSeconds = (long)gap.TotalSeconds
                }));
            flagged++;
        }

        _systemAudit.Record(
            "System.PowerOutageDetected",
            "System",
            "heartbeat",
            JsonSerializer.Serialize(new
            {
                lastSavedAt = lastBeat.Value,
                recoveredAt = now,
                gapSeconds = (long)gap.TotalSeconds,
                sessionsFlagged = flagged
            }));

        // Sessions, audit rows and the heartbeat move forward together: if this save fails the next start retries
        // with the same outage window, so no session is skipped.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _outages.SaveHeartbeatAsync(now, cancellationToken);
        return new OutageRecoveryResult(true, gap, flagged);
    }

    public Task BeatAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        => _outages.SaveHeartbeatAsync(now, cancellationToken);

    public async Task<IReadOnlyList<OutageReviewItem>> ListNeedingReviewAsync(
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var sessions = await _outages.ListNeedingReviewAsync(cancellationToken);
        var deviceIds = sessions
            .Select(s => s.GetActiveSegment()?.DeviceId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var names = await _outages.GetDeviceNamesAsync(deviceIds, cancellationToken);

        return sessions
            .OrderBy(s => s.OutageStartedAt)
            .Select(s =>
            {
                var segment = s.GetActiveSegment();
                var outageStart = s.OutageStartedAt!.Value;
                return new OutageReviewItem(
                    s.Id,
                    segment?.DeviceId,
                    segment is not null && names.TryGetValue(segment.DeviceId, out var n) ? n : null,
                    s.StartedAt,
                    outageStart,
                    s.OutageEndedAt!.Value,
                    s.GetTotalAmount(outageStart),
                    (long)s.GetTotalBillableDuration(outageStart).TotalSeconds);
            })
            .ToList();
    }

    public async Task<Result<OutageApprovalResult>> ApproveAsync(
        ApproveOutageCommand command, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var result = await ApproveCoreAsync(command, now, cancellationToken);
        if (!result.IsSuccess)
            await _rejections.RejectedAsync(command.AdminUserId, "Session.OutageApprovalRejected", "Session", command.SessionId, result.Error, cancellationToken);
        return result;
    }

    private async Task<Result<OutageApprovalResult>> ApproveCoreAsync(
        ApproveOutageCommand command, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (command.SessionId == Guid.Empty || command.EndShiftId == Guid.Empty || command.AdminUserId == Guid.Empty)
            return Result<OutageApprovalResult>.Failure("SessionId, EndShiftId and the acting user are required.");

        if (string.IsNullOrWhiteSpace(command.Note) || command.Note.Trim().Length < 3)
            return Result<OutageApprovalResult>.Failure("A reason is required to set the real end time of an outage session.");

        var session = await _sessions.GetByIdAsync(command.SessionId, cancellationToken);
        if (session is null)
            return Result<OutageApprovalResult>.Failure("Session was not found.");

        if (!session.NeedsReview || session.Status != SessionStatus.Active)
            return Result<OutageApprovalResult>.Failure("This session is not waiting for an outage review.");

        var shift = await _sessions.GetShiftAsync(command.EndShiftId, cancellationToken);
        if (shift is null)
            return Result<OutageApprovalResult>.Failure("End shift was not found.");
        if (shift.Status != ShiftStatus.Open)
            return Result<OutageApprovalResult>.Failure("The end shift must be open.");

        var segment = session.GetActiveSegment();
        if (segment is null)
            return Result<OutageApprovalResult>.Failure("Session has no active segment.");

        var device = await _sessions.GetDeviceForStartAsync(segment.DeviceId, cancellationToken);
        if (device is null)
            return Result<OutageApprovalResult>.Failure("Session device was not found.");

        try
        {
            var outageStart = session.OutageStartedAt;
            var outageEnd = session.OutageEndedAt;
            session.ApproveAfterOutage(command.AdminUserId, command.EndShiftId, command.ActualEndAt, now);
            device.MarkAsAvailable();

            var total = session.GetTotalAmount();
            var seconds = (long)session.GetTotalBillableDuration().TotalSeconds;

            _audit.Record(
                command.AdminUserId,
                "Session.OutageApproved",
                "Session",
                session.Id.ToString(),
                JsonSerializer.Serialize(new
                {
                    actualEndAt = command.ActualEndAt,
                    outageStartedAt = outageStart,
                    outageEndedAt = outageEnd,
                    endShiftId = command.EndShiftId,
                    totalAmount = total,
                    billableSeconds = seconds,
                    note = command.Note
                }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<OutageApprovalResult>.Success(new OutageApprovalResult(session.Id, total, seconds));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<OutageApprovalResult>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<OutageApprovalResult>.Failure(exception.Message);
        }
    }
}
