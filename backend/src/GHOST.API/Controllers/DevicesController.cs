using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Devices.CreateDevice;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

[ApiController]
[Route("api/devices")]
public sealed class DevicesController : ControllerBase
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly CreateDeviceHandler _createDeviceHandler;
    private readonly IUnitOfWork _unitOfWork;

    public DevicesController(
        IDeviceRepository deviceRepository,
        CreateDeviceHandler createDeviceHandler,
        IUnitOfWork unitOfWork)
    {
        _deviceRepository = deviceRepository;
        _createDeviceHandler = createDeviceHandler;
        _unitOfWork = unitOfWork;
    }

    public sealed record DeviceRateRequest(PlayType PlayType, decimal HourlyRate);

    public sealed record CreateDeviceRequest(
        string Name,
        string Category,
        IReadOnlyCollection<DeviceRateRequest>? Rates);

    public sealed record UpdateDeviceRatesRequest(
        IReadOnlyCollection<DeviceRateRequest> Rates);

    [HttpGet]
    public async Task<IActionResult> GetAll(
        CancellationToken cancellationToken)
    {
        var devices = await _deviceRepository.GetAllAsync(cancellationToken);
        return Ok(devices.Select(ToResponse));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateDeviceRequest request,
        CancellationToken cancellationToken)
    {
        var rates = request.Rates?
            .Select(rate => new DeviceRateInput(rate.PlayType, rate.HourlyRate))
            .ToArray() ?? Array.Empty<DeviceRateInput>();

        var result = await _createDeviceHandler.HandleAsync(
            new CreateDeviceCommand(request.Name, request.Category, rates),
            cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return StatusCode(StatusCodes.Status201Created, new { deviceId = result.Value });
    }

    [HttpPost("seed-defaults")]
    public async Task<IActionResult> SeedDefaultDevices(
        CancellationToken cancellationToken)
    {
        var defaults = new (string Name, string Category)[]
        {
            ("PS2", "عادي"),
            ("PS3", "عادي"),
            ("PS4", "عادي"),
            ("PS5", "عادي"),
            ("PS6", "عادي"),
            ("VIP 1", "VIP"),
            ("VIP 2", "VIP"),
            ("VIP 3", "VIP"),
            ("Room 1", "Room"),
            ("Room 2", "Room")
        };

        var existingNames = (await _deviceRepository.GetAllAsync(cancellationToken))
            .Select(device => device.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var (name, category) in defaults)
        {
            if (existingNames.Contains(name))
                continue;

            await _deviceRepository.AddAsync(new Device(name, category), cancellationToken);
            existingNames.Add(name);
            added++;
        }

        if (added > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            added,
            message = added == 0
                ? "Default devices already exist."
                : $"Added {added} default devices. Configure their prices in the admin dashboard."
        });
    }

    [HttpPut("{id:guid}/rates")]
    public async Task<IActionResult> UpdateRates(
        Guid id,
        [FromBody] UpdateDeviceRatesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Rates is null || request.Rates.Count == 0)
            return BadRequest(new { error = "Enter at least one hourly rate." });

        if (request.Rates.Any(rate => !Enum.IsDefined(rate.PlayType)) ||
            request.Rates.GroupBy(rate => rate.PlayType).Any(group => group.Count() > 1))
            return BadRequest(new { error = "Play types must be valid and unique." });

        var device = await _deviceRepository.GetByIdAsync(id, cancellationToken);
        if (device is null)
            return NotFound(new { error = "Device not found." });

        try
        {
            foreach (var rate in request.Rates)
                device.SetRate(rate.PlayType, rate.HourlyRate);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Ok(ToResponse(device));
        }
        catch (BusinessRuleValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    private static object ToResponse(Device device) => new
    {
        device.Id,
        device.Name,
        device.Category,
        Status = device.Status.ToString(),
        device.IsActive,
        Rates = device.Rates.Select(rate => new
        {
            rate.Id,
            PlayType = rate.PlayType.ToString(),
            rate.HourlyRate
        })
    };
}
