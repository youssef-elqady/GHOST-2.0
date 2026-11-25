using GHOST.Application.Alerts;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence;

/// <summary>
/// Alert rules as set-based queries. Each rule is one bounded query over an indexed time column;
/// names are resolved once at the end, so there is no per-row lookup.
/// </summary>
public sealed class AlertQueries : IAlertQueries
{
    private const int MaxPerRule = 200;

    private readonly GHOSTDbContext _db;

    public AlertQueries(GHOSTDbContext db) => _db = db;

    public async Task<IReadOnlyList<AlertDto>> GetAlertsAsync(
        DateTimeOffset rangeFrom, DateTimeOffset rangeTo, AlertThresholds t, CancellationToken cancellationToken = default)
    {
        var alerts = new List<AlertDto>();

        // 1. Closed shifts that are short.
        var shortages = await _db.Shifts.AsNoTracking()
            .Where(s => s.ClosedAt != null && s.ClosedAt >= rangeFrom && s.ClosedAt < rangeTo
                        && s.CashDifference != null && s.CashDifference <= -t.CashShortageAmount)
            .OrderByDescending(s => s.ClosedAt).Take(MaxPerRule)
            .Select(s => new { s.Id, s.StaffUserId, s.ClosedAt, s.CashDifference })
            .ToListAsync(cancellationToken);
        alerts.AddRange(shortages.Select(s => new AlertDto(
            AlertTypes.CashShortage, Severity(-s.CashDifference!.Value, t.CashShortageAmount), s.ClosedAt!.Value,
            "Shift", s.Id, s.StaffUserId, null, -s.CashDifference!.Value, t.CashShortageAmount, null)));

        // 2. Many cancellations inside one shift, attributed to that shift's staff.
        var cancelled = await _db.Sessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Cancelled && s.CancelledAt != null
                        && s.CancelledAt >= rangeFrom && s.CancelledAt < rangeTo && s.EndShiftId != null)
            .GroupBy(s => s.EndShiftId!.Value)
            .Select(g => new { ShiftId = g.Key, Count = g.Count(), Last = g.Max(x => x.CancelledAt) })
            .Where(g => g.Count >= t.CancellationsPerShift)
            .Take(MaxPerRule)
            .ToListAsync(cancellationToken);
        if (cancelled.Count > 0)
        {
            var shiftIds = cancelled.Select(c => c.ShiftId).ToList();
            var staffByShift = await _db.Shifts.AsNoTracking()
                .Where(s => shiftIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.StaffUserId, cancellationToken);
            alerts.AddRange(cancelled.Select(c => new AlertDto(
                AlertTypes.RepeatedCancellations, Severity(c.Count, t.CancellationsPerShift), c.Last!.Value,
                "Shift", c.ShiftId, staffByShift.GetValueOrDefault(c.ShiftId), null, c.Count,
                t.CancellationsPerShift, null)));
        }

        // 3. Big discounts. Integer-free comparison: discount * 100 >= subtotal * percent.
        var discounts = await _db.Sales.AsNoTracking()
            .Where(s => s.PaidAt != null && s.PaidAt >= rangeFrom && s.PaidAt < rangeTo
                        && (s.Status == SaleStatus.Paid || s.Status == SaleStatus.Refunded)
                        && s.Subtotal > 0 && s.DiscountAmount * 100 >= s.Subtotal * t.DiscountPercent)
            .OrderByDescending(s => s.PaidAt).Take(MaxPerRule)
            .Select(s => new { s.Id, s.InvoiceNumber, s.CreatedById, s.PaidAt, s.DiscountAmount, s.Subtotal })
            .ToListAsync(cancellationToken);
        alerts.AddRange(discounts.Select(s =>
        {
            var percent = Math.Round(s.DiscountAmount * 100m / s.Subtotal, 1);
            return new AlertDto(AlertTypes.LargeDiscount, Severity(percent, t.DiscountPercent), s.PaidAt!.Value,
                "Sale", s.Id, s.CreatedById, null, percent, t.DiscountPercent, s.InvoiceNumber);
        }));

        // 4. Large withdrawals, and every withdrawal categorized as Other.
        var other = WithdrawalCategoryOther;
        var withdrawals = await _db.CashTransactions.AsNoTracking()
            .Where(c => c.Type == CashTransactionType.Withdrawal && c.CreatedAt >= rangeFrom && c.CreatedAt < rangeTo
                        && (c.Amount >= t.WithdrawalAmount || c.Category == other))
            .OrderByDescending(c => c.CreatedAt).Take(MaxPerRule)
            .Select(c => new { c.Id, c.CreatedById, c.CreatedAt, c.Amount, c.Category })
            .ToListAsync(cancellationToken);
        alerts.AddRange(withdrawals.Select(c => new AlertDto(
            AlertTypes.LargeWithdrawal,
            c.Amount >= t.WithdrawalAmount ? Severity(c.Amount, t.WithdrawalAmount) : "Info",
            c.CreatedAt, "CashTransaction", c.Id, c.CreatedById, null, c.Amount, t.WithdrawalAmount, c.Category)));

        // 5. Approved stocktakes that lost stock, valued at average cost.
        var losses = await (
                from st in _db.Stocktakes.AsNoTracking()
                where st.Status == StocktakeStatus.Approved && st.DecidedAt != null
                      && st.DecidedAt >= rangeFrom && st.DecidedAt < rangeTo
                from i in st.Items
                join p in _db.Products.AsNoTracking() on i.ProductId equals p.Id
                where i.CountedQuantity != null && i.CountedQuantity < i.SystemQuantity
                group new { i, p } by new { st.Id, st.DecidedAt, st.StartedById } into g
                select new
                {
                    g.Key.Id,
                    g.Key.DecidedAt,
                    g.Key.StartedById,
                    Value = g.Sum(x => (x.i.SystemQuantity - x.i.CountedQuantity!.Value) * x.p.AvgCost)
                })
            .Where(x => x.Value >= t.StocktakeLossValue)
            .Take(MaxPerRule)
            .ToListAsync(cancellationToken);
        alerts.AddRange(losses.Select(l => new AlertDto(
            AlertTypes.StocktakeLoss, Severity(l.Value, t.StocktakeLossValue), l.DecidedAt!.Value,
            "Stocktake", l.Id, l.StartedById, null, Math.Round(l.Value, 2), t.StocktakeLossValue, null)));

        // 6. Backups that failed, and verified backups that could not be copied to the external drive.
        var backups = await _db.BackupRecords.AsNoTracking()
            .Where(b => b.CreatedAt >= rangeFrom && b.CreatedAt < rangeTo
                        && (b.Status == BackupStatus.Failed || b.ExternalCopyError != null))
            .OrderByDescending(b => b.CreatedAt).Take(MaxPerRule)
            .Select(b => new { b.Id, b.CreatedAt, b.Status, b.Error, b.ExternalCopyError })
            .ToListAsync(cancellationToken);
        alerts.AddRange(backups.Select(b => new AlertDto(
            b.Status == BackupStatus.Failed ? AlertTypes.BackupFailed : AlertTypes.ExternalBackupFailed,
            "Critical", b.CreatedAt, "BackupRecord", b.Id, null, null, 0, 0, b.Status == BackupStatus.Failed ? b.Error : b.ExternalCopyError)));

        // 7. Sessions waiting for an Admin after a power cut (always shown, whatever the range).
        var pending = await _db.Sessions.AsNoTracking()
            .Where(s => s.NeedsReview && s.Status == SessionStatus.Active)
            .OrderBy(s => s.OutageStartedAt).Take(MaxPerRule)
            .Select(s => new { s.Id, s.OutageEndedAt })
            .ToListAsync(cancellationToken);
        alerts.AddRange(pending.Select(s => new AlertDto(
            AlertTypes.OutageReviewPending, "Critical", s.OutageEndedAt ?? rangeTo, "Session", s.Id, null, null, 0, 0, null)));

        // 8. Completed sessions with no invoice. Always shown, whatever the range, so it survives logout, page refresh and
        // shift change. It is derived from the data, so it disappears by itself once an invoice (Paid or Refunded) exists.
        // Actor: the Admin who approved it after an outage, else the user who completed it (audit log), else the
        // owner of the shift it ended in.
        var uninvoiced = await _db.Sessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Completed && !s.IsVoided
                        && !_db.Sales.Any(x => x.SessionId == s.Id
                                               && (x.Status == SaleStatus.Paid || x.Status == SaleStatus.Refunded)))
            .OrderBy(s => s.UpdatedAt ?? s.CreatedAt).Take(MaxPerRule)
            .Select(s => new { s.Id, At = s.UpdatedAt ?? s.CreatedAt, s.EndShiftId, s.ReviewedById })
            .ToListAsync(cancellationToken);
        if (uninvoiced.Count > 0)
        {
            var sessionKeys = uninvoiced.Select(u => u.Id.ToString()).ToList();
            var completedBy = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "Session.Completed" && a.EntityType == "Session"
                            && a.EntityId != null && sessionKeys.Contains(a.EntityId) && a.UserId != null)
                .Select(a => new { a.EntityId, a.UserId })
                .ToListAsync(cancellationToken);
            var actorBySession = completedBy
                .GroupBy(a => a.EntityId!)
                .ToDictionary(g => g.Key, g => g.First().UserId!.Value);
            var endShiftIds = uninvoiced.Where(u => u.EndShiftId != null).Select(u => u.EndShiftId!.Value).Distinct().ToList();
            var ownerByShift = await _db.Shifts.AsNoTracking()
                .Where(s => endShiftIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.StaffUserId, cancellationToken);
            alerts.AddRange(uninvoiced.Select(u => new AlertDto(
                AlertTypes.CompletedSessionNotInvoiced, "Warning", u.At, "Session", u.Id,
                u.ReviewedById
                    ?? (actorBySession.TryGetValue(u.Id.ToString(), out var actor) ? actor : (Guid?)null)
                    ?? (u.EndShiftId is { } sid && ownerByShift.TryGetValue(sid, out var owner) ? owner : (Guid?)null),
                null, 0, 0, null)));
        }

        return await AttachNamesAsync(alerts, cancellationToken);
    }

    private const string WithdrawalCategoryOther = "Other";

    /// <summary>Warning at the threshold, Critical at three times it.</summary>
    internal static string Severity(decimal value, decimal threshold)
        => threshold > 0 && value >= threshold * 3 ? "Critical" : "Warning";

    private async Task<IReadOnlyList<AlertDto>> AttachNamesAsync(List<AlertDto> alerts, CancellationToken cancellationToken)
    {
        var ids = alerts.Where(a => a.StaffUserId != null).Select(a => a.StaffUserId!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return alerts;

        var names = await _db.Set<ApplicationUser>().AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        return alerts.Select(a => a with
        {
            StaffName = a.StaffUserId is { } id && names.TryGetValue(id, out var n) ? n : null
        }).ToList();
    }
}
