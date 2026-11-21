using GHOST.Application.Abstractions.CurrentUser;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Enums;

namespace GHOST.API.Security;

/// <summary>
/// Resource ownership checks for shift-scoped operations. Admins may act on any shift;
/// Staff may only act on their own currently open shift.
/// </summary>
public sealed class ShiftAccess
{
    private readonly ICurrentUser _currentUser;
    private readonly ISessionRepository _sessionRepository;

    public ShiftAccess(ICurrentUser currentUser, ISessionRepository sessionRepository)
    {
        _currentUser = currentUser;
        _sessionRepository = sessionRepository;
    }

    public async Task<bool> CanOperateOnShiftAsync(
        Guid shiftId,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return false;

        if (_currentUser.IsAdmin)
            return true;

        if (!_currentUser.IsStaff)
            return false;

        var shift = await _sessionRepository.GetShiftAsync(shiftId, cancellationToken);
        return shift is not null
            && shift.StaffUserId == _currentUser.UserId.Value
            && shift.Status == ShiftStatus.Open;
    }

    /// <summary>Close is allowed on the caller's own shift (Admin: any shift).</summary>
    public async Task<bool> CanCloseShiftAsync(
        Guid shiftId,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return false;

        if (_currentUser.IsAdmin)
            return true;

        if (!_currentUser.IsStaff)
            return false;

        var shift = await _sessionRepository.GetShiftAsync(shiftId, cancellationToken);
        return shift is not null && shift.StaffUserId == _currentUser.UserId.Value;
    }
}
