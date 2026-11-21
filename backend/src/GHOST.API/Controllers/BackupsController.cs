using GHOST.API.Security;
using GHOST.Application.Abstractions.CurrentUser;
using GHOST.Application.Backup;
using GHOST.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GHOST.API.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/backups")]
public sealed class BackupsController : ControllerBase
{
    private readonly BackupHandler _handler;
    private readonly ICurrentUser _currentUser;

    public BackupsController(BackupHandler handler, ICurrentUser currentUser)
    {
        _handler = handler;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 30, CancellationToken cancellationToken = default)
        => Ok(await _handler.ListAsync(take, cancellationToken));

    /// <summary>Runs a backup now and waits for it, including the restore verification.</summary>
    [HttpPost]
    public async Task<IActionResult> Run(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } actorId)
            return Unauthorized();

        var result = await _handler.RunAsync(BackupType.Manual, actorId, cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : BadRequest(new { error = result.Error });
    }
}
