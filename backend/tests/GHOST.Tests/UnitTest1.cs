
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.BusinessDays.OpenBusinessDay;
using GHOST.Domain.Entities;

namespace GHOST.Tests;

public class UnitTest1
{
    [Fact]
    public async Task HandleAsync_ShouldFail_WhenBusinessDayIsAlreadyOpen()
    {
        // Arrange
        var repository = new FakeBusinessDayRepository();
        var unitOfWork = new FakeUnitOfWork();

        var existingDay = new BusinessDay(new DateOnly(2026, 10, 13));
        repository.OpenBusinessDay = existingDay;

        var handler = new OpenBusinessDayHandler(repository, unitOfWork);

        var command = new OpenBusinessDayCommand(
            new DateOnly(2026, 10, 14));

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("already an open business day", result.Error);
        Assert.Empty(repository.AddedBusinessDays);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_ShouldOpenBusinessDay_WhenNoBusinessDayIsOpen()
    {
        // Arrange
        var repository = new FakeBusinessDayRepository();
        var unitOfWork = new FakeUnitOfWork();

        var handler = new OpenBusinessDayHandler(repository, unitOfWork);

        var businessDate = new DateOnly(2026, 10, 14);
        var command = new OpenBusinessDayCommand(businessDate);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        Assert.Single(repository.AddedBusinessDays);
        Assert.Equal(businessDate, repository.AddedBusinessDays[0].BusinessDate);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_ShouldFail_WhenBusinessDateAlreadyExists()
    {
        // Arrange
        var repository = new FakeBusinessDayRepository();
        var unitOfWork = new FakeUnitOfWork();

        var businessDate = new DateOnly(2026, 10, 14);

        repository.AddedBusinessDays.Add(
            new BusinessDay(businessDate));

        var handler = new OpenBusinessDayHandler(repository, unitOfWork);

        var command = new OpenBusinessDayCommand(businessDate);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("already exists", result.Error);
        Assert.Single(repository.AddedBusinessDays);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
    private sealed class FakeBusinessDayRepository : IBusinessDayRepository
    {
        public BusinessDay? OpenBusinessDay { get; set; }

        public List<BusinessDay> AddedBusinessDays { get; } = new();

        public Task<BusinessDay?> GetByDateAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                AddedBusinessDays.FirstOrDefault(
                    day => day.BusinessDate == businessDate));
        }

        public Task<BusinessDay?> GetOpenAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OpenBusinessDay);
        }

        public Task AddAsync(
            BusinessDay businessDay,
            CancellationToken cancellationToken = default)
        {
            AddedBusinessDays.Add(businessDay);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }
}