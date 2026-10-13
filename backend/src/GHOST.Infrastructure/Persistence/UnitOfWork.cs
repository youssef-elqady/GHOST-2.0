
using GHOST.Application.Abstractions.Persistence;

namespace GHOST.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly GHOSTDbContext _dbContext;

    public UnitOfWork(GHOSTDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}