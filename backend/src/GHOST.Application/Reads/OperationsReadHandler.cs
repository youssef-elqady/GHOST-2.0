using GHOST.Application.Abstractions.CurrentUser;

namespace GHOST.Application.Reads;

public enum ReadOutcome { Ok, NotFound, Forbidden, Invalid }

public sealed record ReadResult<T>(ReadOutcome Outcome, T? Value = default, string? Error = null)
{
    public static ReadResult<T> Ok(T value) => new(ReadOutcome.Ok, value);
    public static ReadResult<T> NotFound() => new(ReadOutcome.NotFound);
    public static ReadResult<T> Forbidden() => new(ReadOutcome.Forbidden);
    public static ReadResult<T> Invalid(string error) => new(ReadOutcome.Invalid, default, error);
}

/// <summary>
/// Authorization-aware read use cases. Admin sees everything; Staff only what operating requires:
/// their own shifts and the sessions that are active or touched their shifts. Anything else is Forbidden/NotFound.
/// </summary>
public sealed class OperationsReadHandler
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 25;
    public const int MaxAuditRangeDays = 366;

    private readonly IOperationsReadQueries _queries;
    private readonly ICurrentUser _user;

    public OperationsReadHandler(IOperationsReadQueries queries, ICurrentUser user)
    {
        _queries = queries;
        _user = user;
    }

    private bool Authenticated => _user.IsAuthenticated && _user.UserId is not null && (_user.IsAdmin || _user.IsStaff);

    private static (int Page, int Size) Clamp(int? page, int? size) =>
        (Math.Max(page ?? 1, 1), Math.Clamp(size ?? DefaultPageSize, 1, MaxPageSize));

    public async Task<ReadResult<BusinessDayDto>> GetCurrentBusinessDayAsync(CancellationToken ct)
    {
        if (!Authenticated) return ReadResult<BusinessDayDto>.Forbidden();
        var day = await _queries.GetOpenBusinessDayAsync(ct);
        return day is null ? ReadResult<BusinessDayDto>.NotFound() : ReadResult<BusinessDayDto>.Ok(day);
    }

    public async Task<ReadResult<ShiftDto>> GetCurrentShiftAsync(CancellationToken ct)
    {
        if (!Authenticated) return ReadResult<ShiftDto>.Forbidden();
        var shift = await _queries.GetOpenShiftOfAsync(_user.UserId!.Value, ct);
        return shift is null ? ReadResult<ShiftDto>.NotFound() : ReadResult<ShiftDto>.Ok(shift);
    }

    public async Task<ReadResult<ShiftDto>> GetShiftAsync(Guid id, CancellationToken ct)
    {
        if (!Authenticated) return ReadResult<ShiftDto>.Forbidden();
        var shift = await _queries.GetShiftAsync(id, ct);
        if (shift is null) return ReadResult<ShiftDto>.NotFound();
        if (!_user.IsAdmin && shift.StaffUserId != _user.UserId) return ReadResult<ShiftDto>.NotFound();
        return ReadResult<ShiftDto>.Ok(shift);
    }

    public async Task<ReadResult<PagedResult<ShiftDto>>> ListShiftsAsync(
        ShiftFilter filter, int? page, int? size, CancellationToken ct)
    {
        if (!_user.IsAuthenticated || !_user.IsAdmin) return ReadResult<PagedResult<ShiftDto>>.Forbidden();
        var (p, s) = Clamp(page, size);
        return ReadResult<PagedResult<ShiftDto>>.Ok(await _queries.ListShiftsAsync(filter, p, s, ct));
    }

    public async Task<ReadResult<PagedResult<SessionSummaryDto>>> ListSessionsAsync(
        SessionFilter filter, int? page, int? size, CancellationToken ct)
    {
        if (!Authenticated) return ReadResult<PagedResult<SessionSummaryDto>>.Forbidden();
        var (p, s) = Clamp(page, size);
        Guid? scope = _user.IsAdmin ? null : _user.UserId;
        return ReadResult<PagedResult<SessionSummaryDto>>.Ok(await _queries.ListSessionsAsync(filter, scope, p, s, ct));
    }

    public async Task<ReadResult<SessionDetailDto>> GetSessionAsync(Guid id, CancellationToken ct)
    {
        if (!Authenticated) return ReadResult<SessionDetailDto>.Forbidden();
        Guid? scope = _user.IsAdmin ? null : _user.UserId;
        var dto = await _queries.GetSessionAsync(id, scope, ct);
        return dto is null ? ReadResult<SessionDetailDto>.NotFound() : ReadResult<SessionDetailDto>.Ok(dto);
    }

    public async Task<ReadResult<PagedResult<AuditLogDto>>> ListAuditLogsAsync(
        AuditLogFilter filter, int? page, int? size, CancellationToken ct)
    {
        if (!_user.IsAuthenticated || !_user.IsAdmin) return ReadResult<PagedResult<AuditLogDto>>.Forbidden();
        if (filter.From is not null && filter.To is not null)
        {
            if (filter.From >= filter.To) return ReadResult<PagedResult<AuditLogDto>>.Invalid("from must be before to.");
            if (filter.To - filter.From > TimeSpan.FromDays(MaxAuditRangeDays))
                return ReadResult<PagedResult<AuditLogDto>>.Invalid($"The range cannot exceed {MaxAuditRangeDays} days.");
        }
        var (p, s) = Clamp(page, size);
        return ReadResult<PagedResult<AuditLogDto>>.Ok(await _queries.ListAuditLogsAsync(filter, p, s, ct));
    }

    public async Task<ReadResult<PagedResult<UserDto>>> ListUsersAsync(
        string? role, bool? isActive, int? page, int? size, CancellationToken ct)
    {
        if (!_user.IsAuthenticated || !_user.IsAdmin) return ReadResult<PagedResult<UserDto>>.Forbidden();
        var (p, s) = Clamp(page, size);
        return ReadResult<PagedResult<UserDto>>.Ok(await _queries.ListUsersAsync(role, isActive, p, s, ct));
    }
}
