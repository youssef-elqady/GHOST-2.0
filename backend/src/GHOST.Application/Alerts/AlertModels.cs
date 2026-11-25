namespace GHOST.Application.Alerts;

public static class AlertTypes
{
    public const string CashShortage = "CashShortage";
    public const string RepeatedCancellations = "RepeatedCancellations";
    public const string LargeDiscount = "LargeDiscount";
    public const string LargeWithdrawal = "LargeWithdrawal";
    public const string StocktakeLoss = "StocktakeLoss";
    public const string BackupFailed = "BackupFailed";
    public const string ExternalBackupFailed = "ExternalBackupFailed";
    public const string OutageReviewPending = "OutageReviewPending";

    /// <summary>A session was completed and no invoice (Paid or Refunded) exists for it. Shown until an invoice is issued.</summary>
    public const string CompletedSessionNotInvoiced = "CompletedSessionNotInvoiced";
}

/// <summary>
/// One computed alert. The UI localizes by <see cref="Type"/>; the numbers are in the fields so no text parsing is needed.
/// </summary>
public sealed record AlertDto(
    string Type,
    string Severity,
    DateTimeOffset OccurredAt,
    string EntityType,
    Guid EntityId,
    Guid? StaffUserId,
    string? StaffName,
    decimal Amount,
    decimal Threshold,
    string? Detail);

public sealed record AlertThresholds(
    decimal WithdrawalAmount,
    decimal CashShortageAmount,
    decimal DiscountPercent,
    int CancellationsPerShift,
    decimal StocktakeLossValue);

public interface IAlertQueries
{
    Task<IReadOnlyList<AlertDto>> GetAlertsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        AlertThresholds thresholds,
        CancellationToken cancellationToken = default);
}
