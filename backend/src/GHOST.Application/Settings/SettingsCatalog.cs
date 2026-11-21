using System.Globalization;

namespace GHOST.Application.Settings;

public enum SettingKind
{
    Integer,
    Money
}

public sealed record SettingDefinition(
    string Key,
    SettingKind Kind,
    decimal Min,
    decimal Max,
    decimal Default,
    string Description);

/// <summary>
/// The only settings the system accepts. Unknown keys are rejected so a typo cannot silently
/// create a setting nothing reads. Money values are whole EGP (SRS BD-13).
/// </summary>
public static class SettingsCatalog
{
    public const string CancelWindowSeconds = "Sessions:CancelWindowSeconds";
    public const string WithdrawalAlertThreshold = "Alerts:WithdrawalAmount";
    public const string CashShortageAlertThreshold = "Alerts:CashShortageAmount";
    public const string DiscountAlertPercent = "Alerts:DiscountPercent";
    public const string CancellationsAlertCount = "Alerts:CancellationsPerShift";
    public const string StocktakeLossAlertValue = "Alerts:StocktakeLossValue";

    private static readonly SettingDefinition[] Definitions =
    [
        new(CancelWindowSeconds, SettingKind.Integer, 0, 3600, 120,
            "Seconds after start during which Staff may cancel a session without Admin."),
        new(WithdrawalAlertThreshold, SettingKind.Money, 1, 1_000_000, 500,
            "Alert when a single withdrawal is at least this amount."),
        new(CashShortageAlertThreshold, SettingKind.Money, 1, 1_000_000, 50,
            "Alert when a closed shift is short by at least this amount."),
        new(DiscountAlertPercent, SettingKind.Integer, 1, 100, 20,
            "Alert when an invoice discount is at least this percent of its subtotal."),
        new(CancellationsAlertCount, SettingKind.Integer, 1, 1000, 3,
            "Alert when one shift cancels at least this many sessions."),
        new(StocktakeLossAlertValue, SettingKind.Money, 1, 10_000_000, 200,
            "Alert when an approved stocktake loses at least this value at cost.")
    ];

    public static IReadOnlyList<SettingDefinition> All => Definitions;

    public static SettingDefinition? Find(string? key)
        => Definitions.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));

    /// <summary>Validates a raw value. Returns the normalized text, or an error.</summary>
    public static (string? Value, string? Error) Validate(SettingDefinition definition, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)
            || !decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            return (null, $"{definition.Key} must be a number.");

        if (number != decimal.Truncate(number))
            return (null, $"{definition.Key} must be a whole number.");

        if (number < definition.Min || number > definition.Max)
            return (null, $"{definition.Key} must be between {definition.Min:0} and {definition.Max:0}.");

        return (number.ToString("0", CultureInfo.InvariantCulture), null);
    }
}
