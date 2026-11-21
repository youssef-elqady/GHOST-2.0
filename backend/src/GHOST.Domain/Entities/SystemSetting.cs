namespace GHOST.Domain.Entities;

/// <summary>One admin-editable setting. Keys and value rules are defined by the application catalog.</summary>
public sealed class SystemSetting
{
    public const int MaxKeyLength = 100;
    public const int MaxValueLength = 500;

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid UpdatedById { get; private set; }

    private SystemSetting()
    {
    }

    public SystemSetting(string key, string value, Guid updatedById, DateTimeOffset now)
    {
        Key = key;
        Value = value;
        UpdatedById = updatedById;
        UpdatedAt = now;
    }

    public void Change(string value, Guid updatedById, DateTimeOffset now)
    {
        Value = value;
        UpdatedById = updatedById;
        UpdatedAt = now;
    }
}
