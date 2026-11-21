using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Cash;
using GHOST.Domain.Entities;
using GHOST.Domain.Exceptions;

namespace GHOST.Tests.Cash;

public sealed class ShiftHandoverTests
{
    private static readonly Guid StaffA = Guid.NewGuid();
    private static readonly Guid StaffB = Guid.NewGuid();

    private static (Shift From, Shift To) Shifts(decimal handed, decimal toOpening)
    {
        var day = new BusinessDay(new DateOnly(2026, 11, 21));
        var from = day.OpenShift(StaffA, 100m);
        from.Close(handed, handed == 100m ? null : "count");
        Thread.Sleep(2);
        var to = day.OpenShift(StaffB, toOpening);
        return (from, to);
    }

    [Fact]
    public void Acknowledge_MatchingCash_HasZeroDifference()
    {
        var (from, to) = Shifts(100m, 100m);
        var handover = ShiftHandover.Acknowledge(from, to, 100m, null, StaffB);

        Assert.Equal(0m, handover.Difference);
        Assert.Equal(100m, handover.HandedCash);
        Assert.Null(handover.Note);
    }

    [Fact]
    public void Acknowledge_Mismatch_RequiresNoteAndRecordsDifference()
    {
        var (from, to) = Shifts(100m, 90m);

        Assert.Throws<BusinessRuleValidationException>(() =>
            ShiftHandover.Acknowledge(from, to, 90m, null, StaffB));

        var handover = ShiftHandover.Acknowledge(from, to, 90m, "10 EGP short in drawer", StaffB);
        Assert.Equal(-10m, handover.Difference);
    }

    [Fact]
    public void Acknowledge_ReceivedMustMatchIncomingOpeningCash()
    {
        var (from, to) = Shifts(100m, 100m);
        Assert.Throws<BusinessRuleValidationException>(() =>
            ShiftHandover.Acknowledge(from, to, 120m, "note", StaffB));
    }

    [Fact]
    public void Acknowledge_RejectsOpenOutgoingShiftAndSelfHandover()
    {
        var day = new BusinessDay(new DateOnly(2026, 11, 21));
        var open = day.OpenShift(StaffA, 100m);

        Assert.Throws<BusinessRuleValidationException>(() =>
            ShiftHandover.Acknowledge(open, open, 100m, null, StaffB));

        var (from, to) = Shifts(100m, 100m);
        to.Close(100m, null);
        Assert.Throws<BusinessRuleValidationException>(() =>
            ShiftHandover.Acknowledge(from, to, 100m, null, StaffB));
    }

    [Fact]
    public async Task Handler_SavesAndAuditsDiscrepancy()
    {
        var (from, to) = Shifts(100m, 90m);
        var repo = new Repo(from, to);
        var audit = new Audit();
        var handler = new AcknowledgeHandoverHandler(repo, new Uow(), audit);

        var result = await handler.HandleAsync(new AcknowledgeHandoverCommand(
            from.Id, to.Id, StaffB, 90m, "short"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(-10m, result.Value!.Difference);
        Assert.Single(repo.Added);
        Assert.Equal("Shift.HandoverDiscrepancy", Assert.Single(audit.Actions));
    }

    [Fact]
    public async Task Handler_RejectsSecondHandoverForSameShift()
    {
        var (from, to) = Shifts(100m, 100m);
        var repo = new Repo(from, to) { AlreadyExists = true };
        var handler = new AcknowledgeHandoverHandler(repo, new Uow(), new Audit());

        var result = await handler.HandleAsync(new AcknowledgeHandoverCommand(
            from.Id, to.Id, StaffB, 100m, null));

        Assert.False(result.IsSuccess);
        Assert.Empty(repo.Added);
    }

    private sealed class Repo(Shift from, Shift to) : IShiftRepository
    {
        public bool AlreadyExists { get; init; }
        public List<ShiftHandover> Added { get; } = [];

        public Task<Shift?> GetAsync(Guid shiftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Shift?>(shiftId == from.Id ? from : shiftId == to.Id ? to : null);
        public Task<Shift?> GetWithCashAsync(Guid shiftId, CancellationToken cancellationToken = default) =>
            GetAsync(shiftId, cancellationToken);
        public Task<IReadOnlyList<Shift>> GetOpenShiftsForUserWithCashAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Shift>>(
                new[] { from, to }.Where(s => s.StaffUserId == userId && s.Status == GHOST.Domain.Enums.ShiftStatus.Open).ToList());
        public Task<bool> HandoverExistsAsync(Guid fromShiftId, Guid toShiftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AlreadyExists);
        public Task AddHandoverAsync(ShiftHandover handover, CancellationToken cancellationToken = default)
        {
            Added.Add(handover);
            return Task.CompletedTask;
        }
    }

    private sealed class Audit : IAuditTrail
    {
        public List<string> Actions { get; } = [];
        public void Record(Guid actorUserId, string action, string entityType, string entityId, string? newValuesJson = null) =>
            Actions.Add(action);
    }

    private sealed class Uow : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
}
