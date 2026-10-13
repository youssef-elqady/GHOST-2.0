
using GHOST.Domain.Entities;

namespace GHOST.Application.Abstractions.Persistence;

public interface IBusinessDayRepository
{
    Task<BusinessDay?> GetByDateAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default);

    Task<BusinessDay?> GetOpenAsync(
        CancellationToken cancellationToken = default);

    Task AddAsync(
        BusinessDay businessDay,
        CancellationToken cancellationToken = default);
}