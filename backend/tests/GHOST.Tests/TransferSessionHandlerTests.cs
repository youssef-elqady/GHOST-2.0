using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Sessions;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;

namespace GHOST.Tests;

public sealed class TransferSessionHandlerTests
{
    [Fact]
    public async Task HandleAsync_TransfersSessionAndUpdatesBothDevices()
    {
        var session = CreateSession(out var currentDevice, out var destinationDevice);
        var repository = new FakeSessionRepository(session, currentDevice, destinationDevice);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new TransferSessionHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new TransferSessionCommand(
            session.Id, destinationDevice.Id, PlayType.Multi));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(destinationDevice.Id, result.Value!.DeviceId);
        Assert.Equal("Running", result.Value.Status);
        Assert.Equal(DeviceStatus.Available, currentDevice.Status);
        Assert.Equal(DeviceStatus.Running, destinationDevice.Status);
        Assert.Equal(2, session.Segments.Count);
        Assert.Equal(60m, session.Segments.First().AppliedHourlyRate);
        Assert.Equal(40m, session.Segments.Last().AppliedHourlyRate);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_RejectsTransferWhileSessionIsPaused()
    {
        var session = CreateSession(out var currentDevice, out var destinationDevice);
        session.PauseActiveDevice();
        currentDevice.MarkAsPaused();

        var repository = new FakeSessionRepository(session, currentDevice, destinationDevice);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new TransferSessionHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new TransferSessionCommand(
            session.Id, destinationDevice.Id, PlayType.Multi));

        Assert.False(result.IsSuccess);
        Assert.Contains("Resume the session", result.Error);
        Assert.Equal(DeviceStatus.Paused, currentDevice.Status);
        Assert.Equal(DeviceStatus.Available, destinationDevice.Status);
        Assert.Single(session.Segments);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_RejectsDestinationThatIsNotAvailable()
    {
        var session = CreateSession(out var currentDevice, out var destinationDevice);
        destinationDevice.MarkAsReserved();

        var repository = new FakeSessionRepository(session, currentDevice, destinationDevice);
        var unitOfWork = new FakeUnitOfWork();
        var handler = new TransferSessionHandler(repository, unitOfWork);

        var result = await handler.HandleAsync(new TransferSessionCommand(
            session.Id, destinationDevice.Id, PlayType.Multi));

        Assert.False(result.IsSuccess);
        Assert.Contains("must be available", result.Error);
        Assert.Equal(DeviceStatus.Running, currentDevice.Status);
        Assert.Equal(DeviceStatus.Reserved, destinationDevice.Status);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    private static Session CreateSession(out Device currentDevice, out Device destinationDevice)
    {
        currentDevice = new Device("PS1", "PlayStation");
        currentDevice.SetRate(PlayType.Single, 60m);
        currentDevice.SetRate(PlayType.Multi, 60m);
        currentDevice.MarkAsRunning(PlayType.Single);

        destinationDevice = new Device("VIP 1", "VIP");
        destinationDevice.SetRate(PlayType.Single, 50m);
        destinationDevice.SetRate(PlayType.Multi, 40m);

        return new Session(
            Guid.NewGuid(),
            currentDevice.Id,
            PlayType.Single,
            60m);
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        private readonly Session _session;
        private readonly Dictionary<Guid, Device> _devices;

        public FakeSessionRepository(Session session, params Device[] devices)
        {
            _session = session;
            _devices = devices.ToDictionary(device => device.Id);
        }

        public Task<Session?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<Session?>(sessionId == _session.Id ? _session : null);

        public Task<Session?> GetActiveByDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult<Session?>(_session.Status == SessionStatus.Active &&
                _session.GetActiveSegment()?.DeviceId == deviceId ? _session : null);

        public Task<Device?> GetDeviceForStartAsync(Guid deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult(_devices.GetValueOrDefault(deviceId));

        public Task<Shift?> GetShiftAsync(Guid shiftId, CancellationToken cancellationToken = default)
            => Task.FromResult<Shift?>(null);

        public Task AddAsync(Session session, CancellationToken cancellationToken = default)
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
}
