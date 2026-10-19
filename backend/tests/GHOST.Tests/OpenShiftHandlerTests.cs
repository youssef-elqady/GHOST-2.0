using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.BusinessDays.OpenShift;
using GHOST.Application.Common.Exceptions;
using GHOST.Domain.Entities;

namespace GHOST.Tests;

public sealed class OpenShiftHandlerTests
{
    [Fact]
    public async Task HandleAsync_OpensShiftWithExpectedOpeningCash()
    {
        var businessDay = new BusinessDay(new DateOnly(2026, 10, 19));
        var repository = new FakeBusinessDayRepository(businessDay);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new OpenShiftHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new OpenShiftCommand(
            businessDay.Id, Guid.NewGuid(), 250m));

        Assert.True(result.IsSuccess, result.Error);
        var shift = Assert.Single(businessDay.Shifts);
        Assert.Equal(result.Value, shift.Id);
        Assert.Equal(250m, shift.OpeningCash);
        Assert.Equal(250m, shift.ExpectedCash);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_RejectsOpeningShiftForClosedBusinessDay()
    {
        var businessDay = new BusinessDay(new DateOnly(2026, 10, 19));
        businessDay.Close();
        var repository = new FakeBusinessDayRepository(businessDay);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new OpenShiftHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new OpenShiftCommand(
            businessDay.Id, Guid.NewGuid(), 250m));

        Assert.False(result.IsSuccess);
        Assert.Contains("closed business day", result.Error);
        Assert.Empty(businessDay.Shifts);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_ReturnsFailureWhenPersistenceConflicts()
    {
        var businessDay = new BusinessDay(new DateOnly(2026, 10, 19));
        var repository = new FakeBusinessDayRepository(businessDay);
        var unitOfWork = new ConflictUnitOfWork();
        var handler = new OpenShiftHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new OpenShiftCommand(
            businessDay.Id, Guid.NewGuid(), 250m));

        Assert.False(result.IsSuccess);
        Assert.Contains("conflicts", result.Error);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    private sealed class FakeBusinessDayRepository : IBusinessDayRepository
    {
        private readonly BusinessDay _businessDay;

        public FakeBusinessDayRepository(BusinessDay businessDay)
        {
            _businessDay = businessDay;
        }

        public Task<BusinessDay?> GetForShiftOpeningAsync(
            Guid businessDayId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<BusinessDay?>(
                businessDayId == _businessDay.Id ? _businessDay : null);

        public Task<BusinessDay?> GetByDateAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult<BusinessDay?>(
                businessDate == _businessDay.BusinessDate ? _businessDay : null);

        public Task<BusinessDay?> GetOpenAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<BusinessDay?>(_businessDay.IsClosed ? null : _businessDay);

        public Task AddAsync(
            BusinessDay businessDay,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(1);
        }
    }

    private sealed class ConflictUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            throw new PersistenceConflictException(
                "This operation conflicts with an existing record.");
        }
    }
}
