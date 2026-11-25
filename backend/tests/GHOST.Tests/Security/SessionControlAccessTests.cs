using GHOST.API.Security;
using GHOST.Application.Abstractions.CurrentUser;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Domain.Entities;

namespace GHOST.Tests.Security;

/// <summary>Pause, resume and transfer: any running session, but Staff need an open shift of their own (decision Nov 25).</summary>
public sealed class SessionControlAccessTests
{
    private sealed class User(Guid? id, bool admin, bool staff) : ICurrentUser
    {
        public Guid? UserId { get; } = id;
        public bool IsAuthenticated => id is not null;
        public bool IsAdmin { get; } = admin;
        public bool IsStaff { get; } = staff;
    }

    private sealed class Shifts(params Shift[] open) : IShiftRepository
    {
        public Task<Shift?> GetWithCashAsync(Guid shiftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Shift?> GetAsync(Guid shiftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Shift>> GetOpenShiftsForUserWithCashAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Shift>>(open.Where(s => s.StaffUserId == userId).ToList());
        public Task<bool> HandoverExistsAsync(Guid fromShiftId, Guid toShiftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddHandoverAsync(ShiftHandover handover, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static Shift OpenShift(Guid staffId) => new BusinessDay(new DateOnly(2026, 11, 25)).OpenShift(staffId, 100m);

    private static ShiftAccess Access(ICurrentUser user, IShiftRepository shifts) => new(user, null!, shifts);

    [Fact]
    public async Task Staff_WithAnOpenShift_MayControlAnySession()
    {
        var staff = Guid.NewGuid();
        Assert.True(await Access(new User(staff, false, true), new Shifts(OpenShift(staff))).CanControlRunningSessionsAsync(default));
    }

    [Fact]
    public async Task Staff_WithoutAnOpenShift_MayNot_EvenIfSomeoneElseHasOne()
    {
        var staff = Guid.NewGuid();
        Assert.False(await Access(new User(staff, false, true), new Shifts(OpenShift(Guid.NewGuid()))).CanControlRunningSessionsAsync(default));
    }

    [Fact]
    public async Task Anonymous_MayNot_AndAdminIsNotRestricted()
    {
        Assert.False(await Access(new User(null, false, false), new Shifts()).CanControlRunningSessionsAsync(default));
        Assert.True(await Access(new User(Guid.NewGuid(), true, false), new Shifts()).CanControlRunningSessionsAsync(default));
    }
}
