using GHOST.Domain.Common;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

/// <summary>
/// Documented cash handover between two shifts (SRS FR-12, BD-09). The outgoing staff counted
/// <see cref="HandedCash"/> when closing; the incoming staff confirms what they received.
/// A mismatch is recorded with a mandatory note instead of being silently absorbed.
/// </summary>
public sealed class ShiftHandover : BaseEntity
{
    public const int MaxNoteLength = 500;

    public Guid FromShiftId { get; private set; }

    public Guid ToShiftId { get; private set; }

    public decimal HandedCash { get; private set; }

    public decimal ReceivedCash { get; private set; }

    /// <summary>ReceivedCash minus HandedCash. Negative means the incoming staff received less.</summary>
    public decimal Difference { get; private set; }

    public string? Note { get; private set; }

    public Guid AcknowledgedById { get; private set; }

    public DateTimeOffset AcknowledgedAt { get; private set; }

    private ShiftHandover()
    {
    }

    public static ShiftHandover Acknowledge(
        Shift fromShift,
        Shift toShift,
        decimal receivedCash,
        string? note,
        Guid acknowledgedById)
    {
        ArgumentNullException.ThrowIfNull(fromShift);
        ArgumentNullException.ThrowIfNull(toShift);

        if (fromShift.Id == toShift.Id)
            throw new BusinessRuleValidationException("A shift cannot hand over to itself.");

        if (fromShift.Status != ShiftStatus.Closed || fromShift.ActualCash is null)
            throw new BusinessRuleValidationException("The outgoing shift must be closed with counted cash.");

        if (toShift.Status != ShiftStatus.Open)
            throw new BusinessRuleValidationException("The incoming shift must be open.");

        if (toShift.OpenedAt < fromShift.ClosedAt)
            throw new BusinessRuleValidationException("The incoming shift must open after the outgoing shift closed.");

        if (acknowledgedById == Guid.Empty)
            throw new BusinessRuleValidationException("The acknowledging user is required.");

        if (receivedCash < 0 || decimal.Round(receivedCash, 2) != receivedCash)
            throw new BusinessRuleValidationException("Received cash must be zero or more with at most two decimals.");

        if (receivedCash != toShift.OpeningCash)
            throw new BusinessRuleValidationException(
                "Received cash must equal the incoming shift's opening cash so the drawer ledger stays consistent.");

        var handed = fromShift.ActualCash.Value;
        var difference = receivedCash - handed;
        var trimmed = note?.Trim();

        if (difference != 0 && string.IsNullOrWhiteSpace(trimmed))
            throw new BusinessRuleValidationException(
                $"A note is required when received cash differs from handed cash (difference: {difference:N2}).");

        if (trimmed?.Length > MaxNoteLength)
            throw new BusinessRuleValidationException($"Note cannot exceed {MaxNoteLength} characters.");

        return new ShiftHandover
        {
            FromShiftId = fromShift.Id,
            ToShiftId = toShift.Id,
            HandedCash = handed,
            ReceivedCash = receivedCash,
            Difference = difference,
            Note = string.IsNullOrEmpty(trimmed) ? null : trimmed,
            AcknowledgedById = acknowledgedById,
            AcknowledgedAt = DateTimeOffset.UtcNow
        };
    }
}
