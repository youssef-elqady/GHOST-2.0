using System.Reflection;
using GHOST.API.Controllers;
using GHOST.API.Security;
using GHOST.Application.Abstractions.CurrentUser;
using GHOST.Application.Reads;
using GHOST.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace GHOST.Tests.Security;

public sealed class ReadApiAuthorizationTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private sealed class User(Guid? id, bool admin, bool staff) : ICurrentUser
    {
        public Guid? UserId { get; } = id;
        public bool IsAuthenticated => id is not null;
        public bool IsAdmin => admin;
        public bool IsStaff => staff;
    }

    private sealed class Queries : IOperationsReadQueries
    {
        public Guid? LastSessionScope = Guid.Empty;
        public int Calls;
        public int LastPageSize;
        public ShiftDto? Shift;

        public Task<ShiftDto?> GetOpenShiftOfAsync(Guid staffUserId, CancellationToken ct) { Calls++; return Task.FromResult(Shift?.StaffUserId == staffUserId ? Shift : null); }
        public Task<ShiftDto?> GetShiftAsync(Guid shiftId, CancellationToken ct) { Calls++; return Task.FromResult(Shift); }
        public Task<PagedResult<ShiftDto>> ListShiftsAsync(ShiftFilter f, int page, int pageSize, CancellationToken ct) { Calls++; LastPageSize = pageSize; return Task.FromResult(new PagedResult<ShiftDto>([], page, pageSize, 0)); }
        public Task<PagedResult<SessionSummaryDto>> ListSessionsAsync(SessionFilter f, Guid? scope, int page, int pageSize, CancellationToken ct) { Calls++; LastSessionScope = scope; return Task.FromResult(new PagedResult<SessionSummaryDto>([], page, pageSize, 0)); }
        public Task<SessionDetailDto?> GetSessionAsync(Guid id, Guid? scope, CancellationToken ct) { Calls++; LastSessionScope = scope; return Task.FromResult<SessionDetailDto?>(null); }
        public Task<PagedResult<AuditLogDto>> ListAuditLogsAsync(AuditLogFilter f, int page, int pageSize, CancellationToken ct) { Calls++; return Task.FromResult(new PagedResult<AuditLogDto>([], page, pageSize, 0)); }
        public Task<PagedResult<UserDto>> ListUsersAsync(string? role, bool? isActive, int page, int pageSize, CancellationToken ct) { Calls++; return Task.FromResult(new PagedResult<UserDto>([], page, pageSize, 0)); }
    }

    private static ShiftDto ShiftOf(Guid staff) => new(Guid.NewGuid(), Guid.NewGuid(), staff, "x", ShiftStatus.Open,
        DateTimeOffset.UtcNow, null, 0, 0, null, null, null);

    private static OperationsReadHandler Handler(Queries q, ICurrentUser u) => new(q, u);
    private static readonly AuditLogFilter NoAudit = new(null, null, null, null, null, null);

    [Fact]
    public async Task Staff_CannotListShifts_AuditLogs_OrUsers_AndNoQueryRuns()
    {
        var q = new Queries();
        var h = Handler(q, new User(Me, false, true));
        Assert.Equal(ReadOutcome.Forbidden, (await h.ListShiftsAsync(new ShiftFilter(null, null, null, null), null, null, default)).Outcome);
        Assert.Equal(ReadOutcome.Forbidden, (await h.ListAuditLogsAsync(NoAudit, null, null, default)).Outcome);
        Assert.Equal(ReadOutcome.Forbidden, (await h.ListUsersAsync(null, null, null, null, default)).Outcome);
        Assert.Equal(0, q.Calls);
    }

    [Fact]
    public async Task Anonymous_IsForbiddenEverywhere()
    {
        var q = new Queries();
        var h = Handler(q, new User(null, false, false));
        Assert.Equal(ReadOutcome.Forbidden, (await h.GetCurrentShiftAsync(default)).Outcome);
        Assert.Equal(ReadOutcome.Forbidden, (await h.GetShiftAsync(Guid.NewGuid(), default)).Outcome);
        Assert.Equal(ReadOutcome.Forbidden, (await h.ListSessionsAsync(new SessionFilter(null, null, null, null, null), null, null, default)).Outcome);
        Assert.Equal(ReadOutcome.Forbidden, (await h.GetSessionAsync(Guid.NewGuid(), default)).Outcome);
        Assert.Equal(0, q.Calls);
    }

    [Fact]
    public async Task Admin_CanListEverything_AndPageSizeIsClamped()
    {
        var q = new Queries();
        var h = Handler(q, new User(Me, true, false));
        Assert.Equal(ReadOutcome.Ok, (await h.ListShiftsAsync(new ShiftFilter(null, null, null, null), 0, 100000, default)).Outcome);
        Assert.Equal(OperationsReadHandler.MaxPageSize, q.LastPageSize);
        Assert.Equal(ReadOutcome.Ok, (await h.ListAuditLogsAsync(NoAudit, null, null, default)).Outcome);
        Assert.Equal(ReadOutcome.Ok, (await h.ListUsersAsync(null, null, null, null, default)).Outcome);
    }

    [Fact]
    public async Task Staff_CannotReadAnotherStaffShift_ItLooksLikeNotFound()
    {
        var q = new Queries { Shift = ShiftOf(Other) };
        var h = Handler(q, new User(Me, false, true));
        Assert.Equal(ReadOutcome.NotFound, (await h.GetShiftAsync(q.Shift.Id, default)).Outcome);
    }

    [Fact]
    public async Task Staff_CanReadOwnShift_AdminCanReadAny()
    {
        var q = new Queries { Shift = ShiftOf(Me) };
        Assert.Equal(ReadOutcome.Ok, (await Handler(q, new User(Me, false, true)).GetShiftAsync(q.Shift.Id, default)).Outcome);
        q.Shift = ShiftOf(Other);
        Assert.Equal(ReadOutcome.Ok, (await Handler(q, new User(Me, true, false)).GetShiftAsync(q.Shift.Id, default)).Outcome);
    }

    [Fact]
    public async Task CurrentShift_IsAlwaysTheCallersOwn()
    {
        var q = new Queries { Shift = ShiftOf(Other) };
        Assert.Equal(ReadOutcome.NotFound, (await Handler(q, new User(Me, false, true)).GetCurrentShiftAsync(default)).Outcome);
        Assert.Equal(ReadOutcome.Ok, (await Handler(q, new User(Other, false, true)).GetCurrentShiftAsync(default)).Outcome);
    }

    [Fact]
    public async Task Sessions_StaffAreScopedToTheirUser_AdminIsUnscoped()
    {
        var q = new Queries();
        var f = new SessionFilter(null, null, null, null, null);
        await Handler(q, new User(Me, false, true)).ListSessionsAsync(f, null, null, default);
        Assert.Equal(Me, q.LastSessionScope);
        await Handler(q, new User(Me, false, true)).GetSessionAsync(Guid.NewGuid(), default);
        Assert.Equal(Me, q.LastSessionScope);
        await Handler(q, new User(Me, true, false)).ListSessionsAsync(f, null, null, default);
        Assert.Null(q.LastSessionScope);
    }

    [Fact]
    public async Task AuditLogs_RejectInvertedOrHugeRanges()
    {
        var h = Handler(new Queries(), new User(Me, true, false));
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(ReadOutcome.Invalid, (await h.ListAuditLogsAsync(NoAudit with { From = now, To = now.AddDays(-1) }, null, null, default)).Outcome);
        Assert.Equal(ReadOutcome.Invalid, (await h.ListAuditLogsAsync(NoAudit with { From = now.AddDays(-400), To = now }, null, null, default)).Outcome);
    }

    [Theory]
    [InlineData(typeof(AuditLogsController), nameof(AuditLogsController.List), AuthorizationPolicies.AdminOnly)]
    [InlineData(typeof(UsersController), nameof(UsersController.List), AuthorizationPolicies.AdminOnly)]
    [InlineData(typeof(ShiftsController), nameof(ShiftsController.List), AuthorizationPolicies.AdminOnly)]
    [InlineData(typeof(ShiftsController), nameof(ShiftsController.Current), AuthorizationPolicies.StaffOrAdmin)]
    [InlineData(typeof(ShiftsController), nameof(ShiftsController.Get), AuthorizationPolicies.StaffOrAdmin)]
    [InlineData(typeof(SessionReadsController), nameof(SessionReadsController.List), AuthorizationPolicies.StaffOrAdmin)]
    [InlineData(typeof(SessionReadsController), nameof(SessionReadsController.Get), AuthorizationPolicies.StaffOrAdmin)]
    public void Endpoint_DeclaresTheExpectedPolicy(Type controller, string action, string policy)
    {
        var method = controller.GetMethod(action)!;
        var onAction = method.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy);
        var onClass = controller.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy);
        // The action-level policy (if any) is the stricter one; otherwise the class policy applies.
        var effective = onAction.Any() ? onAction : onClass;
        Assert.Contains(policy, effective);
    }
}
