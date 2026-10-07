using GHOST.Domain.Common;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

public sealed class DeviceRate : BaseEntity
{
    public Guid DeviceId { get; private set; }

    public PlayType PlayType { get; private set; }

    public decimal HourlyRate { get; private set; }

    private DeviceRate()
    {
    }

    internal DeviceRate(
        Guid deviceId,
        PlayType playType,
        decimal hourlyRate)
    {
        if (deviceId == Guid.Empty)
            throw new BusinessRuleValidationException(
                "Device ID cannot be empty.");

        ValidateRate(hourlyRate);

        DeviceId = deviceId;
        PlayType = playType;
        HourlyRate = hourlyRate;
    }

    internal void UpdateRate(decimal hourlyRate)
    {
        ValidateRate(hourlyRate);

        HourlyRate = hourlyRate;
        MarkAsUpdated();
    }

    private static void ValidateRate(decimal hourlyRate)
    {
        if (hourlyRate <= 0)
            throw new BusinessRuleValidationException(
                "Hourly rate must be greater than zero.");
    }
}