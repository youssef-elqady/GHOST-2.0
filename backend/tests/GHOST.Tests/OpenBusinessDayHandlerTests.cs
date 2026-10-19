using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.BusinessDays.OpenBusinessDay;
using GHOST.Application.Common.Exceptions;
using GHOST.Domain.Entities;

namespace GHOST.Tests;

public sealed class OpenBusinessDayHandlerTests
{
    [Fact]
    public async Task HandleAsync_CreatesBusinessDayWhenNoneIsOpen()
    {
        var repository = new FakeBusinessDayRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new OpenBusinessDayHandler(repository, unitOfWork);
        var date = new DateOnly(2026, 10, 19);

        var result = await handler.HandleAsync(new OpenBusinessDayCommand(date));

        Assert.True(result.IsSuccess, result.Error);
        var businessDay = Assert.Single(repository.BusinessDays);
        Assert.Equal(result.Value, businessDay.Id);
        Assert.Equal(date, businessDay.BusinessDate);
        Assert.False(businessDay.IsClosed);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_RejectsWhenAnotherBusinessDayIsOpen()
    {
        var openDay = new BusinessDay(new DateOnly(2026, 10, 18));
        var repository = new FakeBusinessDayRepository(openDay);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new OpenBusinessDayHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(
            new OpenBusinessDayCommand(new DateOnly(2026, 10, 19)));

        Assert.False(result.IsSuccess);
        Assert.Contains("already an open business day", result.Error);
        Assert.Single(repository.BusinessDays);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_RejectsDuplicateBusinessDate()
    {
        var existingDay = new BusinessDay(new DateOnly(2026, 10, 19));
        existingDay.Close();
        var repository = new FakeBusinessDayRepository(existingDay);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new OpenBusinessDayHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(
            new OpenBusinessDayCommand(new DateOnly(2026, 10, 19)));

        Assert.False(result.IsSuccess);
        Assert.Contains("already exists", result.Error);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_RejectsDefaultDate()
    {
        var repository = new FakeBusinessDayRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new OpenBusinessDayHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(
            new OpenBusinessDayCommand(default));

        Assert.False(result.IsSuccess);
        Assert.Contains("date is required", result.Error);
        Assert.Empty(repository.BusinessDays);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_ReturnsFailureWhenPersistenceConflicts()
    {
        var repository = new FakeBusinessDayRepository();
        var unitOfWork = new ConflictUnitOfWork();
        var handler = new OpenBusinessDayHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(
            new OpenBusinessDayCommand(new DateOnly(2026, 10, 19)));

        Assert.False(result.IsSuccess);
        Assert.Contains("conflicts", result.Error);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    private sealed class FakeBusinessDayRepository : IBusinessDayRepository
    {
        public List<BusinessDay> BusinessDays { get; } = new();

        public FakeBusinessDayRepository(params BusinessDay[] businessDays)
        {
            BusinessDays.AddRange(businessDays);
        }

        public Task<BusinessDay?> GetForShiftOpeningAsync(
            Guid businessDayId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(BusinessDays.FirstOrDefault(day => day.Id == businessDayId));

        public Task<BusinessDay?> GetByDateAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult(BusinessDays.FirstOrDefault(day => day.BusinessDate == businessDate));

        public Task<BusinessDay?> GetOpenAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(BusinessDays.FirstOrDefault(day => !day.IsClosed));

        public Task AddAsync(
            BusinessDay businessDay,
            CancellationToken cancellationToken = default)
        {
            BusinessDays.Add(businessDay);
            return Task.CompletedTask;
        }
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public virtual Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(1);
        }
    }

    private sealed class ConflictUnitOfWork : FakeUnitOfWork
    {
        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            _ = base.SaveChangesAsync(cancellationToken);
            throw new PersistenceConflictException(
                "This operation conflicts with an existing record.");
        }
    }
}
