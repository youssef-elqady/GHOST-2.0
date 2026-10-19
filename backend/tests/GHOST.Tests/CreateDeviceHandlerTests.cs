using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Devices.CreateDevice;
using GHOST.Application.Common.Exceptions;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;

namespace GHOST.Tests;

public sealed class CreateDeviceHandlerTests
{
    [Fact]
    public async Task HandleAsync_CreatesDeviceWithConfiguredRates()
    {
        var repository = new FakeDeviceRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateDeviceHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new CreateDeviceCommand(
            "PS5 VIP 1",
            "VIP",
            new[]
            {
                new DeviceRateInput(PlayType.Single, 40m),
                new DeviceRateInput(PlayType.Multi, 50m)
            }));

        Assert.True(result.IsSuccess, result.Error);
        var device = Assert.Single(repository.Devices);
        Assert.Equal(result.Value, device.Id);
        Assert.Equal("PS5 VIP 1", device.Name);
        Assert.Equal(40m, device.GetRate(PlayType.Single));
        Assert.Equal(50m, device.GetRate(PlayType.Multi));
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_RejectsWhenNoRatesAreProvided()
    {
        var repository = new FakeDeviceRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateDeviceHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new CreateDeviceCommand(
            "PS5 VIP 1",
            "VIP",
            Array.Empty<DeviceRateInput>()));

        Assert.False(result.IsSuccess);
        Assert.Contains("At least one hourly rate", result.Error);
        Assert.Empty(repository.Devices);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_RejectsDuplicatePlayTypeRates()
    {
        var repository = new FakeDeviceRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateDeviceHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new CreateDeviceCommand(
            "PS5 VIP 1",
            "VIP",
            new[]
            {
                new DeviceRateInput(PlayType.Single, 40m),
                new DeviceRateInput(PlayType.Single, 45m)
            }));

        Assert.False(result.IsSuccess);
        Assert.Contains("only have one rate", result.Error);
        Assert.Empty(repository.Devices);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_RejectsNonPositiveHourlyRate()
    {
        var repository = new FakeDeviceRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateDeviceHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new CreateDeviceCommand(
            "PS5 VIP 1",
            "VIP",
            new[] { new DeviceRateInput(PlayType.Single, 0m) }));

        Assert.False(result.IsSuccess);
        Assert.Contains("greater than zero", result.Error);
        Assert.Empty(repository.Devices);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    private sealed class FakeDeviceRepository : IDeviceRepository
    {
        public List<Device> Devices { get; } = new();

        public Task<IReadOnlyList<Device>> GetAllAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Device>>(Devices);

        public Task AddAsync(
            Device device,
            CancellationToken cancellationToken = default)
        {
            Devices.Add(device);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(1);
        }
    }
}
