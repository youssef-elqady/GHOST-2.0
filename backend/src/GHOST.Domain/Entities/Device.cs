using GHOST.Domain.Common;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;

namespace GHOST.Domain.Entities;

public sealed class Device : BaseEntity
{
    private readonly List<DeviceRate> _rates = new();

    public string Name { get; private set; }

    public string Category { get; private set; }

    public DeviceStatus Status { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<DeviceRate> Rates => _rates.AsReadOnly();

    private Device()
    {
        Name = null!;
        Category = null!;
    }

    public Device(
        string name,
        string category)
    {
        ValidateName(name);
        ValidateCategory(category);

        Name = name.Trim();
        Category = category.Trim();
        Status = DeviceStatus.Available;
        IsActive = true;
    }

    public void Rename(string name)
    {
        ValidateName(name);

        Name = name.Trim();
        MarkAsUpdated();
    }

    public void ChangeCategory(string category)
    {
        ValidateCategory(category);

        Category = category.Trim();
        MarkAsUpdated();
    }

    public void SetRate(
        PlayType playType,
        decimal hourlyRate)
    {
        EnsureActive();

        var existingRate = _rates
            .FirstOrDefault(rate => rate.PlayType == playType);

        if (existingRate is not null)
        {
            existingRate.UpdateRate(hourlyRate);
        }
        else
        {
            _rates.Add(
                new DeviceRate(
                    Id,
                    playType,
                    hourlyRate));
        }

        MarkAsUpdated();
    }

    public decimal GetRate(PlayType playType)
    {
        EnsureActive();

        var rate = _rates
            .FirstOrDefault(rate => rate.PlayType == playType);

        if (rate is null)
        {
            throw new BusinessRuleValidationException(
                $"No rate is configured for play type '{playType}'.");
        }

        return rate.HourlyRate;
    }

    public void MarkAsRunning(PlayType playType)
    {
        EnsureActive();

        if (Status != DeviceStatus.Available &&
            Status != DeviceStatus.Reserved)
        {
            throw new BusinessRuleValidationException(
                $"Device cannot start running from status '{Status}'.");
        }

        _ = GetRate(playType);

        Status = DeviceStatus.Running;
        MarkAsUpdated();
    }

    public void MarkAsPaused()
    {
        EnsureActive();

        if (Status != DeviceStatus.Running)
        {
            throw new BusinessRuleValidationException(
                "Only a running device can be paused.");
        }

        Status = DeviceStatus.Paused;
        MarkAsUpdated();
    }

    public void MarkAsResumed()
    {
        EnsureActive();

        if (Status != DeviceStatus.Paused)
        {
            throw new BusinessRuleValidationException(
                "Only a paused device can be resumed.");
        }

        Status = DeviceStatus.Running;
        MarkAsUpdated();
    }

    public void MarkAsAvailable()
    {
        EnsureActive();

        if (Status != DeviceStatus.Running &&
            Status != DeviceStatus.Paused &&
            Status != DeviceStatus.Reserved)
        {
            throw new BusinessRuleValidationException(
                $"Device cannot be marked as available from status '{Status}'.");
        }

        Status = DeviceStatus.Available;
        MarkAsUpdated();
    }

    public void MarkAsReserved()
    {
        EnsureActive();

        if (Status != DeviceStatus.Available)
        {
            throw new BusinessRuleValidationException(
                "Only an available device can be reserved.");
        }

        Status = DeviceStatus.Reserved;
        MarkAsUpdated();
    }

    public void MarkAsMaintenance()
    {
        EnsureActive();

        if (Status != DeviceStatus.Available)
        {
            throw new BusinessRuleValidationException(
                $"Device can enter maintenance only from '{DeviceStatus.Available}'.");
        }

        Status = DeviceStatus.Maintenance;
        MarkAsUpdated();
    }

    public void MarkAsOffline()
    {
        EnsureActive();

        if (Status != DeviceStatus.Available)
        {
            throw new BusinessRuleValidationException(
                $"Device can go offline only from '{DeviceStatus.Available}'.");
        }

        Status = DeviceStatus.Offline;
        MarkAsUpdated();
    }

    public void Deactivate()
    {
        if (!IsActive)
            return;

        if (Status == DeviceStatus.Running ||
            Status == DeviceStatus.Paused ||
            Status == DeviceStatus.Reserved)
        {
            throw new BusinessRuleValidationException(
                "A device with an active or reserved operation cannot be deactivated.");
        }

        IsActive = false;
        Status = DeviceStatus.Offline;
        MarkAsUpdated();
    }

    public void Reactivate()
    {
        if (IsActive)
            return;

        IsActive = true;
        Status = DeviceStatus.Available;
        MarkAsUpdated();
    }

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw new BusinessRuleValidationException(
                "Inactive device cannot perform operational actions.");
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessRuleValidationException(
                "Device name is required.");
        }

        if (name.Trim().Length > 100)
        {
            throw new BusinessRuleValidationException(
                "Device name cannot exceed 100 characters.");
        }
    }

    private static void ValidateCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new BusinessRuleValidationException(
                "Device category is required.");
        }

        if (category.Trim().Length > 100)
        {
            throw new BusinessRuleValidationException(
                "Device category cannot exceed 100 characters.");
        }
    }
}