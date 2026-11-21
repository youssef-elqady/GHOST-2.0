using GHOST.Domain.Enums;

namespace GHOST.Application.Reads;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record ShiftDto(
    Guid Id, Guid BusinessDayId, Guid StaffUserId, string? StaffName, ShiftStatus Status,
    DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, decimal OpeningCash, decimal ExpectedCash,
    decimal? ActualCash, decimal? CashDifference, string? ClosingReason);

public sealed record BusinessDayDto(Guid Id, DateOnly BusinessDate, DateTimeOffset OpenedAt);

public sealed record PauseDto(DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string? Reason);

public sealed record SegmentDto(
    Guid Id, Guid DeviceId, PlayType PlayType, decimal AppliedHourlyRate,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt, IReadOnlyList<PauseDto> Pauses);

public sealed record SessionSummaryDto(
    Guid Id, Guid StartShiftId, Guid? EndShiftId, SessionStatus Status, DateTimeOffset StartedAt,
    DateTimeOffset? CancelledAt, bool IsVoided, Guid? CurrentDeviceId, int SegmentCount);

public sealed record SessionDetailDto(
    Guid Id, Guid StartShiftId, Guid? EndShiftId, SessionStatus Status, DateTimeOffset StartedAt,
    DateTimeOffset? CancelledAt, string? CancellationReason, bool IsVoided, IReadOnlyList<SegmentDto> Segments);

public sealed record AuditLogDto(
    Guid Id, Guid? UserId, string? UserName, string ActorType, string Action, string EntityType,
    string? EntityId, string? OldValues, string? NewValues, DateTimeOffset At);

public sealed record UserDto(
    Guid Id, string UserName, string FullName, string? Email, string? Role, bool IsActive,
    bool MustChangePassword, DateTimeOffset CreatedAt);

public sealed record ShiftFilter(DateTimeOffset? From, DateTimeOffset? To, Guid? StaffUserId, ShiftStatus? Status);

public sealed record SessionFilter(Guid? ShiftId, SessionStatus? Status, Guid? DeviceId, DateTimeOffset? From, DateTimeOffset? To);

public sealed record AuditLogFilter(
    DateTimeOffset? From, DateTimeOffset? To, Guid? UserId, string? EntityType, string? EntityId, string? Action);

/// <summary>Read-only projections. Every list is bounded and paged; no query loads entities per row.</summary>
public interface IOperationsReadQueries
{
    Task<BusinessDayDto?> GetOpenBusinessDayAsync(CancellationToken ct);
    Task<ShiftDto?> GetOpenShiftOfAsync(Guid staffUserId, CancellationToken ct);
    Task<ShiftDto?> GetShiftAsync(Guid shiftId, CancellationToken ct);
    Task<PagedResult<ShiftDto>> ListShiftsAsync(ShiftFilter filter, int page, int pageSize, CancellationToken ct);

    /// <param name="visibleToStaffUserId">When set, only active sessions and sessions started or ended in this user's shifts.</param>
    Task<PagedResult<SessionSummaryDto>> ListSessionsAsync(
        SessionFilter filter, Guid? visibleToStaffUserId, int page, int pageSize, CancellationToken ct);
    Task<SessionDetailDto?> GetSessionAsync(Guid sessionId, Guid? visibleToStaffUserId, CancellationToken ct);

    Task<PagedResult<AuditLogDto>> ListAuditLogsAsync(AuditLogFilter filter, int page, int pageSize, CancellationToken ct);
    Task<PagedResult<UserDto>> ListUsersAsync(string? role, bool? isActive, int page, int pageSize, CancellationToken ct);
}
