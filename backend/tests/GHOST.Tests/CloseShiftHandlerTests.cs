using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.BusinessDays.CloseShift;
using GHOST.Application.Common.Exceptions;
using GHOST.Domain.Entities;

namespace GHOST.Tests;

public sealed class CloseShiftHandlerTests
{
    [Fact]
    public async Task HandleAsync_ClosesShiftAndReturnsCashDifference()
    {
        var day = new BusinessDay(new DateOnly(2026, 10, 19));
        var shift = day.OpenShift(Guid.NewGuid(), 250m);
        shift.UpdateExpectedCash(400m);
        var repository = new FakeBusinessDayRepository(day);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CloseShiftHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new CloseShiftCommand(
            day.Id, shift.Id, 375m, "مشتريات نقدية مسجلة"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Closed", result.Value.Status);
        Assert.Equal(400m, result.Value.ExpectedCash);
        Assert.Equal(375m, result.Value.ActualCash);
        Assert.Equal(-25m, result.Value.CashDifference);
        Assert.Equal("مشتريات نقدية مسجلة", result.Value.ClosingReason);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_RejectsCashDifferenceWithoutReason()
    {
        var day = new BusinessDay(new DateOnly(2026, 10, 19));
        var shift = day.OpenShift(Guid.NewGuid(), 250m);
        shift.UpdateExpectedCash(400m);
        var repository = new FakeBusinessDayRepository(day);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CloseShiftHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new CloseShiftCommand(
            day.Id, shift.Id, 375m, null));

        Assert.False(result.IsSuccess);
        Assert.Contains("reason is required", result.Error);
        Assert.Equal("Open", shift.Status.ToString());
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_ReturnsFailureWhenPersistenceConflicts()
    {
        var day = new BusinessDay(new DateOnly(2026, 10, 19));
        var shift = day.OpenShift(Guid.NewGuid(), 250m);
        var repository = new FakeBusinessDayRepository(day);
        var unitOfWork = new ConflictUnitOfWork();
        var handler = new CloseShiftHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new CloseShiftCommand(
            day.Id, shift.Id, 250m, null));

        Assert.False(result.IsSuccess);
        Assert.Contains("conflicts", result.Error);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_RejectsShiftFromAnotherBusinessDay()
    {
        var day = new BusinessDay(new DateOnly(2026, 10, 19));
        var otherDay = new BusinessDay(new DateOnly(2026, 10, 20));
        var shift = otherDay.OpenShift(Guid.NewGuid(), 250m);
        var repository = new FakeBusinessDayRepository(day);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CloseShiftHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new CloseShiftCommand(
            day.Id, shift.Id, 250m, null));

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error);
        Assert.Equal("Open", shift.Status.ToString());
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    private sealed class FakeBusinessDayRepository : IBusinessDayRepository
    {
        private readonly BusinessDay _day;

        public FakeBusinessDayRepository(BusinessDay day) => _day = day;

        public Task<BusinessDay?> GetForShiftOpeningAsync(
            Guid businessDayId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<BusinessDay?>(
                businessDayId == _day.Id ? _day : null);

        public Task<BusinessDay?> GetByDateAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult<BusinessDay?>(
                businessDate == _day.BusinessDate ? _day : null);

        public Task<BusinessDay?> GetOpenAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<BusinessDay?>(_day.IsClosed ? null : _day);

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
