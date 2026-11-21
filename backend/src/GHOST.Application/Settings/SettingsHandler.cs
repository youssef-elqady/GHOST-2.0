using System.Globalization;
using System.Text.Json;
using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Result;
using GHOST.Domain.Entities;

namespace GHOST.Application.Settings;

public sealed record SettingDto(
    string Key,
    string Description,
    decimal Value,
    decimal Default,
    decimal Min,
    decimal Max,
    bool IsDefault,
    DateTimeOffset? UpdatedAt);

/// <summary>Reads and edits system settings (Admin). Reads fall back to catalog defaults.</summary>
public sealed class SettingsHandler
{
    private readonly ISettingsRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _audit;
    private readonly TimeProvider _time;

    public SettingsHandler(ISettingsRepository settings, IUnitOfWork unitOfWork, IAuditTrail audit, TimeProvider time)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _time = time;
    }

    /// <summary>All values keyed by setting key, defaults filled in. One query.</summary>
    public async Task<IReadOnlyDictionary<string, decimal>> GetValuesAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _settings.ListAsync(cancellationToken);
        return Merge(stored);
    }

    public async Task<IReadOnlyList<SettingDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var stored = (await _settings.ListAsync(cancellationToken)).ToDictionary(s => s.Key);
        return SettingsCatalog.All.Select(d =>
        {
            var has = stored.TryGetValue(d.Key, out var row) && TryParse(row.Value, out _);
            var value = has ? Parse(stored[d.Key].Value) : d.Default;
            return new SettingDto(d.Key, d.Description, value, d.Default, d.Min, d.Max, !has,
                has ? stored[d.Key].UpdatedAt : null);
        }).ToList();
    }

    public async Task<Result<SettingDto>> UpdateAsync(
        Guid actorUserId, string? key, string? rawValue, CancellationToken cancellationToken = default)
    {
        var definition = SettingsCatalog.Find(key);
        if (definition is null)
            return Result<SettingDto>.Failure("Unknown setting.");

        var (value, error) = SettingsCatalog.Validate(definition, rawValue);
        if (error is not null)
            return Result<SettingDto>.Failure(error);

        var now = _time.GetUtcNow();
        var existing = await _settings.GetAsync(definition.Key, cancellationToken);
        var oldValue = existing?.Value;
        if (existing is null)
            await _settings.AddAsync(new SystemSetting(definition.Key, value!, actorUserId, now), cancellationToken);
        else
            existing.Change(value!, actorUserId, now);

        _audit.Record(actorUserId, "Setting.Changed", "SystemSetting", definition.Key,
            JsonSerializer.Serialize(new { oldValue, newValue = value }));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var number = Parse(value!);
        return Result<SettingDto>.Success(new SettingDto(
            definition.Key, definition.Description, number, definition.Default, definition.Min, definition.Max,
            false, now));
    }

    private static IReadOnlyDictionary<string, decimal> Merge(IReadOnlyList<SystemSetting> stored)
    {
        var map = SettingsCatalog.All.ToDictionary(d => d.Key, d => d.Default);
        foreach (var row in stored)
        {
            if (map.ContainsKey(row.Key) && TryParse(row.Value, out var number))
                map[row.Key] = number;
        }

        return map;
    }

    private static bool TryParse(string text, out decimal number)
        => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number);

    private static decimal Parse(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);
}
