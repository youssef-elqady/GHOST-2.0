using GHOST.API.Security;
using GHOST.Application.Reads;
using GHOST.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

internal static class ReadResponse
{
    public static IActionResult From<T>(ControllerBase c, ReadResult<T> r) => r.Outcome switch
    {
        ReadOutcome.Ok => c.Ok(r.Value),
        ReadOutcome.NotFound => c.NotFound(new { error = "Not found." }),
        ReadOutcome.Invalid => c.BadRequest(new { error = r.Error }),
        _ => c.Forbid()
    };
}

[ApiController]
[Authorize(Policy = AuthorizationPolicies.StaffOrAdmin)]
[Route("api/business-days")]
public sealed class BusinessDayReadsController : ControllerBase
{
    private readonly OperationsReadHandler _reads;
    public BusinessDayReadsController(OperationsReadHandler reads) => _reads = reads;

    /// <summary>The open business day (needed to open a shift), or 404 when none is open.</summary>
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct) =>
        ReadResponse.From(this, await _reads.GetCurrentBusinessDayAsync(ct));
}

[ApiController]
[Authorize(Policy = AuthorizationPolicies.StaffOrAdmin)]
[Route("api/shifts")]
public sealed class ShiftsController : ControllerBase
{
    private readonly OperationsReadHandler _reads;
    public ShiftsController(OperationsReadHandler reads) => _reads = reads;

    /// <summary>The caller's own open shift, or 404 when none.</summary>
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct) =>
        ReadResponse.From(this, await _reads.GetCurrentShiftAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        ReadResponse.From(this, await _reads.GetShiftAsync(id, ct));

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> List(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] Guid? staffUserId,
        [FromQuery] ShiftStatus? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        ReadResponse.From(this, await _reads.ListShiftsAsync(new ShiftFilter(from, to, staffUserId, status), page, pageSize, ct));
}

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/audit-logs")]
public sealed class AuditLogsController : ControllerBase
{
    private readonly OperationsReadHandler _reads;
    public AuditLogsController(OperationsReadHandler reads) => _reads = reads;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] Guid? userId,
        [FromQuery] string? entityType, [FromQuery] string? entityId, [FromQuery] string? action,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        ReadResponse.From(this, await _reads.ListAuditLogsAsync(
            new AuditLogFilter(from, to, userId, entityType, entityId, action), page, pageSize, ct));
}

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly OperationsReadHandler _reads;
    public UsersController(OperationsReadHandler reads) => _reads = reads;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? role, [FromQuery] bool? isActive, [FromQuery] int? page, [FromQuery] int? pageSize,
        CancellationToken ct) =>
        ReadResponse.From(this, await _reads.ListUsersAsync(role, isActive, page, pageSize, ct));
}

[ApiController]
[Authorize(Policy = AuthorizationPolicies.StaffOrAdmin)]
[Route("api/sessions")]
public sealed class SessionReadsController : ControllerBase
{
    private readonly OperationsReadHandler _reads;
    public SessionReadsController(OperationsReadHandler reads) => _reads = reads;

    /// <summary>Admin: all sessions. Staff: active sessions plus sessions started or ended in their own shifts.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? shiftId, [FromQuery] SessionStatus? status, [FromQuery] Guid? deviceId,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        ReadResponse.From(this, await _reads.ListSessionsAsync(
            new SessionFilter(shiftId, status, deviceId, from, to), page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        ReadResponse.From(this, await _reads.GetSessionAsync(id, ct));
}
