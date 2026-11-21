using GHOST.Application.Reads;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence;

public sealed class OperationsReadQueries : IOperationsReadQueries
{
    private readonly GHOSTDbContext _db;

    public OperationsReadQueries(GHOSTDbContext db) => _db = db;

    private IQueryable<ShiftDto> ShiftProjection(IQueryable<Shift> shifts) =>
        from s in shifts
        join u in _db.Users.AsNoTracking() on s.StaffUserId equals u.Id into users
        from u in users.DefaultIfEmpty()
        select new ShiftDto(s.Id, s.BusinessDayId, s.StaffUserId, u == null ? null : u.FullName, s.Status,
            s.OpenedAt, s.ClosedAt, s.OpeningCash, s.ExpectedCash, s.ActualCash, s.CashDifference, s.ClosingReason);

    public Task<BusinessDayDto?> GetOpenBusinessDayAsync(CancellationToken ct) =>
        _db.BusinessDays.AsNoTracking().Where(b => !b.IsClosed).OrderByDescending(b => b.OpenedAt)
            .Select(b => new BusinessDayDto(b.Id, b.BusinessDate, b.OpenedAt)).FirstOrDefaultAsync(ct);

    public Task<ShiftDto?> GetOpenShiftOfAsync(Guid staffUserId, CancellationToken ct) =>
        ShiftProjection(_db.Shifts.AsNoTracking().Where(s => s.StaffUserId == staffUserId && s.Status == ShiftStatus.Open))
            .OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync(ct);

    public Task<ShiftDto?> GetShiftAsync(Guid shiftId, CancellationToken ct) =>
        ShiftProjection(_db.Shifts.AsNoTracking().Where(s => s.Id == shiftId)).FirstOrDefaultAsync(ct);

    public async Task<PagedResult<ShiftDto>> ListShiftsAsync(ShiftFilter f, int page, int pageSize, CancellationToken ct)
    {
        var q = _db.Shifts.AsNoTracking().AsQueryable();
        if (f.From is { } from) q = q.Where(s => s.OpenedAt >= from);
        if (f.To is { } to) q = q.Where(s => s.OpenedAt < to);
        if (f.StaffUserId is { } staff) q = q.Where(s => s.StaffUserId == staff);
        if (f.Status is { } status) q = q.Where(s => s.Status == status);
        var total = await q.CountAsync(ct);
        var items = await ShiftProjection(q.OrderByDescending(s => s.OpenedAt).Skip((page - 1) * pageSize).Take(pageSize))
            .OrderByDescending(s => s.OpenedAt).ToListAsync(ct);
        return new PagedResult<ShiftDto>(items, page, pageSize, total);
    }

    private IQueryable<Session> VisibleSessions(Guid? staffUserId)
    {
        var q = _db.Sessions.AsNoTracking().AsQueryable();
        if (staffUserId is { } me)
        {
            var mine = _db.Shifts.Where(s => s.StaffUserId == me).Select(s => s.Id);
            q = q.Where(s => s.Status == SessionStatus.Active
                             || mine.Contains(s.StartShiftId)
                             || (s.EndShiftId != null && mine.Contains(s.EndShiftId.Value)));
        }
        return q;
    }

    public async Task<PagedResult<SessionSummaryDto>> ListSessionsAsync(
        SessionFilter f, Guid? visibleToStaffUserId, int page, int pageSize, CancellationToken ct)
    {
        var q = VisibleSessions(visibleToStaffUserId);
        if (f.ShiftId is { } shiftId) q = q.Where(s => s.StartShiftId == shiftId || s.EndShiftId == shiftId);
        if (f.Status is { } status) q = q.Where(s => s.Status == status);
        if (f.DeviceId is { } device) q = q.Where(s => s.Segments.Any(g => g.DeviceId == device));
        if (f.From is { } from) q = q.Where(s => s.CreatedAt >= from);
        if (f.To is { } to) q = q.Where(s => s.CreatedAt < to);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(s => s.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new SessionSummaryDto(
                s.Id, s.StartShiftId, s.EndShiftId, s.Status, s.CreatedAt, s.CancelledAt, s.IsVoided,
                s.Segments.OrderByDescending(g => g.StartedAt).Select(g => (Guid?)g.DeviceId).FirstOrDefault(),
                s.Segments.Count()))
            .ToListAsync(ct);
        return new PagedResult<SessionSummaryDto>(items, page, pageSize, total);
    }

    public async Task<SessionDetailDto?> GetSessionAsync(Guid sessionId, Guid? visibleToStaffUserId, CancellationToken ct)
    {
        var s = await VisibleSessions(visibleToStaffUserId)
            .Include(x => x.Segments).ThenInclude(g => g.Pauses)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == sessionId, ct);
        if (s is null) return null;
        var segments = s.Segments.OrderBy(g => g.StartedAt).Select(g => new SegmentDto(
            g.Id, g.DeviceId, g.PlayType, g.AppliedHourlyRate, g.StartedAt, g.EndedAt,
            g.Pauses.OrderBy(p => p.StartedAt).Select(p => new PauseDto(p.StartedAt, p.EndedAt, p.Reason)).ToList())).ToList();
        return new SessionDetailDto(s.Id, s.StartShiftId, s.EndShiftId, s.Status, s.CreatedAt, s.CancelledAt,
            s.CancellationReason, s.IsVoided, segments);
    }

    public async Task<PagedResult<AuditLogDto>> ListAuditLogsAsync(AuditLogFilter f, int page, int pageSize, CancellationToken ct)
    {
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (f.From is { } from) q = q.Where(a => a.At >= from);
        if (f.To is { } to) q = q.Where(a => a.At < to);
        if (f.UserId is { } user) q = q.Where(a => a.UserId == user);
        if (!string.IsNullOrWhiteSpace(f.EntityType)) { var t = f.EntityType.Trim(); q = q.Where(a => a.EntityType == t); }
        if (!string.IsNullOrWhiteSpace(f.EntityId)) { var i = f.EntityId.Trim(); q = q.Where(a => a.EntityId == i); }
        if (!string.IsNullOrWhiteSpace(f.Action)) { var act = f.Action.Trim(); q = q.Where(a => a.Action == act); }
        var total = await q.CountAsync(ct);
        var items = await (
            from a in q.OrderByDescending(a => a.At).Skip((page - 1) * pageSize).Take(pageSize)
            join u in _db.Users.AsNoTracking() on a.UserId equals u.Id into users
            from u in users.DefaultIfEmpty()
            select new AuditLogDto(a.Id, a.UserId, u == null ? null : u.FullName, a.ActorType, a.Action, a.EntityType,
                a.EntityId, a.OldValues, a.NewValues, a.At))
            .OrderByDescending(a => a.At).ToListAsync(ct);
        return new PagedResult<AuditLogDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<UserDto>> ListUsersAsync(string? role, bool? isActive, int page, int pageSize, CancellationToken ct)
    {
        var users = _db.Users.AsNoTracking().AsQueryable();
        if (isActive is { } active) users = users.Where(u => u.IsActive == active);
        var withRole =
            from u in users
            join ur in _db.UserRoles on u.Id equals ur.UserId into urs
            from ur in urs.DefaultIfEmpty()
            join r in _db.Roles on ur.RoleId equals r.Id into rs
            from r in rs.DefaultIfEmpty()
            select new { User = u, Role = r == null ? null : r.Name };
        if (!string.IsNullOrWhiteSpace(role)) { var wanted = role.Trim(); withRole = withRole.Where(x => x.Role == wanted); }
        var total = await withRole.CountAsync(ct);
        var items = await withRole.OrderBy(x => x.User.FullName).ThenBy(x => x.User.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new UserDto(x.User.Id, x.User.UserName!, x.User.FullName, x.User.Email, x.Role,
                x.User.IsActive, x.User.MustChangePassword, x.User.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<UserDto>(items, page, pageSize, total);
    }
}
