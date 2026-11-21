using GHOST.Domain.Entities;

namespace GHOST.Application.Abstractions.Persistence;

public interface IShiftRepository
{
    /// <summary>Loads a shift together with its cash ledger entries.</summary>
    Task<Shift?> GetWithCashAsync(
        Guid shiftId,
        CancellationToken cancellationToken = default);

    Task<Shift?> GetAsync(
        Guid shiftId,
        CancellationToken cancellationToken = default);

    /// <summary>The shift this user currently has open (with its cash ledger), or null. Used to decide where a refund posts.</summary>
    Task<Shift?> GetOpenShiftForUserWithCashAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Shift?>(null);

    /// <summary>True when either shift already takes part in a recorded handover.</summary>
    Task<bool> HandoverExistsAsync(
        Guid fromShiftId,
        Guid toShiftId,
        CancellationToken cancellationToken = default);

    Task AddHandoverAsync(
        ShiftHandover handover,
        CancellationToken cancellationToken = default);
}
