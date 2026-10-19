using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Result;
using GHOST.Application.Devices.CreateDevice;
using GHOST.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

[ApiController]
[Route("api/devices")]
public sealed class DevicesController : ControllerBase
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly CreateDeviceHandler _createDeviceHandler;

    public DevicesController(
        IDeviceRepository deviceRepository,
        CreateDeviceHandler createDeviceHandler)
    {
        _deviceRepository = deviceRepository;
        _createDeviceHandler = createDeviceHandler;
    }

    public sealed record DeviceRateRequest(PlayType PlayType, decimal HourlyRate);

    public sealed record CreateDeviceRequest(
        string Name,
        string Category,
        IReadOnlyCollection<DeviceRateRequest> Rates);

    [HttpGet]
    public async Task<IActionResult> GetAll(
        CancellationToken cancellationToken)
    {
        var devices = await _deviceRepository.GetAllAsync(cancellationToken);

        return Ok(devices.Select(device => new
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
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateDeviceRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateDeviceCommand(
            request.Name,
            request.Category,
            request.Rates
                .Select(rate => new DeviceRateInput(
                    rate.PlayType,
                    rate.HourlyRate))
                .ToArray());

        var result = await _createDeviceHandler.HandleAsync(
            command,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return StatusCode(
            StatusCodes.Status201Created,
            new { deviceId = result.Value });
    }
}
