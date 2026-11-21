using System.Text.Json;
using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Common.Result;
using GHOST.Domain.Entities;
using GHOST.Domain.Exceptions;

namespace GHOST.Application.Cash;

public sealed record AcknowledgeHandoverCommand(
    Guid FromShiftId,
    Guid ToShiftId,
    Guid ActorUserId,
    decimal ReceivedCash,
    string? Note);

public sealed record HandoverSnapshot(
    Guid HandoverId,
    Guid FromShiftId,
    Guid ToShiftId,
    decimal HandedCash,
    decimal ReceivedCash,
    decimal Difference,
    string? Note);

/// <summary>
/// The incoming staff confirms the cash they received from the previous shift (SRS FR-12).
/// A difference is recorded with a note and audited as a discrepancy for the Admin.
/// </summary>
public sealed class AcknowledgeHandoverHandler
{
    private readonly IShiftRepository _shiftRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _auditTrail;

    public AcknowledgeHandoverHandler(
        IShiftRepository shiftRepository,
        IUnitOfWork unitOfWork,
        IAuditTrail auditTrail)
    {
        _shiftRepository = shiftRepository;
        _unitOfWork = unitOfWork;
        _auditTrail = auditTrail;
    }

    public async Task<Result<HandoverSnapshot>> HandleAsync(
        AcknowledgeHandoverCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.FromShiftId == Guid.Empty || command.ToShiftId == Guid.Empty ||
            command.ActorUserId == Guid.Empty)
        {
            return Result<HandoverSnapshot>.Failure("FromShiftId, ToShiftId and the acting user are required.");
        }

        var from = await _shiftRepository.GetAsync(command.FromShiftId, cancellationToken);
        var to = await _shiftRepository.GetAsync(command.ToShiftId, cancellationToken);
        if (from is null || to is null)
            return Result<HandoverSnapshot>.Failure("Shift was not found.");

        if (await _shiftRepository.HandoverExistsAsync(from.Id, to.Id, cancellationToken))
            return Result<HandoverSnapshot>.Failure("A handover was already recorded for one of these shifts.");

        try
        {
            var handover = ShiftHandover.Acknowledge(
                from, to, command.ReceivedCash, command.Note, command.ActorUserId);

            await _shiftRepository.AddHandoverAsync(handover, cancellationToken);

            _auditTrail.Record(
                command.ActorUserId,
                handover.Difference == 0 ? "Shift.HandoverAcknowledged" : "Shift.HandoverDiscrepancy",
                "ShiftHandover",
                handover.Id.ToString(),
                JsonSerializer.Serialize(new
                {
                    fromShiftId = from.Id,
                    toShiftId = to.Id,
                    handed = handover.HandedCash,
                    received = handover.ReceivedCash,
                    difference = handover.Difference
                }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<HandoverSnapshot>.Success(new HandoverSnapshot(
                handover.Id,
                from.Id,
                to.Id,
                handover.HandedCash,
                handover.ReceivedCash,
                handover.Difference,
                handover.Note));
        }
        catch (BusinessRuleValidationException exception)
        {
            return Result<HandoverSnapshot>.Failure(exception.Message);
        }
        catch (PersistenceConflictException exception)
        {
            return Result<HandoverSnapshot>.Failure(exception.Message);
        }
    }
}
