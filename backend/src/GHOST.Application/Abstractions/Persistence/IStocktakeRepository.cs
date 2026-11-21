using GHOST.Domain.Entities;

namespace GHOST.Application.Abstractions.Persistence;

public interface IStocktakeRepository
{
    Task<Stocktake?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> OpenStocktakeExistsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Stocktake>> ListAsync(StocktakeStatus? status, CancellationToken cancellationToken = default);

    Task AddAsync(Stocktake stocktake, CancellationToken cancellationToken = default);

    /// <summary>Next internal EAN-13 style barcode number from the database sequence.</summary>
    Task<long> NextInternalBarcodeNumberAsync(CancellationToken cancellationToken = default);
}
