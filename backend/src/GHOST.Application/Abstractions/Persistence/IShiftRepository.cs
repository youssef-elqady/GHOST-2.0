using GHOST.Domain.Entities;

namespace GHOST.Application.Abstractions.Persistence;

public interface IShiftRepository
{
    /// <summary>Loads a shift together with its cash ledger entries.</summary>
    Task<Shift?> GetWithCashAsync(
        Guid shiftId,
        CancellationToken cancellationToken = default);
}
