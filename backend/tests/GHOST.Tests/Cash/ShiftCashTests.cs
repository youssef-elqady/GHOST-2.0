using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Cash;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Domain.Exceptions;
using GHOST.Infrastructure.Persistence;
using GHOST.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Tests.Cash;

public sealed class ShiftCashTests
{
    private static readonly Guid Staff = Guid.NewGuid();

    private static Shift NewShift(decimal opening = 500m) =>
        new BusinessDay(new DateOnly(2026, 11, 21)).OpenShift(Staff, opening);

    [Fact]
    public void OpeningShift_WritesOpeningBalanceEntryMatchingExpectedCash()
    {
        var shift = NewShift(250m);

        var entry = Assert.Single(shift.CashTransactions);
        Assert.Equal(CashTransactionType.OpeningBalance, entry.Type);
        Assert.Equal(CashDirection.In, entry.Direction);
        Assert.Equal(250m, entry.Amount);
        Assert.Equal(250m, shift.ExpectedCash);
    }

    [Fact]
    public void OpeningShiftWithZeroCash_IsAllowed()
    {
        var shift = NewShift(0m);
        Assert.Equal(0m, shift.ExpectedCash);
    }

    [Fact]
    public void RecordCash_MovesExpectedCashBySignedAmount()
    {
        var shift = NewShift(500m);

        shift.RecordCash(CashTransactionType.Expense, CashDirection.Out, 120.50m, Staff, "cleaning", "Cleaning");
        shift.RecordCash(CashTransactionType.SaleCash, CashDirection.In, 80m, Staff);

        Assert.Equal(459.50m, shift.ExpectedCash);
        Assert.Equal(3, shift.CashTransactions.Count);
    }

    [Fact]
    public void RecordCash_RejectsWrongDirectionNonPositiveAndMissingReason()
    {
        var shift = NewShift();

        Assert.Throws<BusinessRuleValidationException>(() =>
            shift.RecordCash(CashTransactionType.Expense, CashDirection.In, 10m, Staff, "x"));
        Assert.Throws<BusinessRuleValidationException>(() =>
            shift.RecordCash(CashTransactionType.Expense, CashDirection.Out, 0m, Staff, "x"));
        Assert.Throws<BusinessRuleValidationException>(() =>
            shift.RecordCash(CashTransactionType.Expense, CashDirection.Out, -5m, Staff, "x"));
        Assert.Throws<BusinessRuleValidationException>(() =>
            shift.RecordCash(CashTransactionType.Expense, CashDirection.Out, 10m, Staff, "  "));
        Assert.Throws<BusinessRuleValidationException>(() =>
            shift.RecordCash(CashTransactionType.Expense, CashDirection.Out, 10.005m, Staff, "x"));
        Assert.Throws<BusinessRuleValidationException>(() =>
            shift.RecordCash(CashTransactionType.OpeningBalance, CashDirection.In, 10m, Staff));
        Assert.Equal(500m, shift.ExpectedCash);
    }

    [Fact]
    public void RecordCash_CannotOverdrawDrawerOrWriteToClosedShift()
    {
        var shift = NewShift(100m);

        Assert.Throws<BusinessRuleValidationException>(() =>
            shift.RecordCash(CashTransactionType.Withdrawal, CashDirection.Out, 100.01m, Staff, "x"));
        Assert.Equal(100m, shift.ExpectedCash);

        shift.Close(100m, null);
        Assert.Throws<BusinessRuleValidationException>(() =>
            shift.RecordCash(CashTransactionType.SaleCash, CashDirection.In, 5m, Staff));
    }

    [Fact]
    public void RecordCash_IdempotencyKeyReturnsSameEntryWithoutSecondMovement()
    {
        var shift = NewShift(500m);

        var first = shift.RecordCash(
            CashTransactionType.Expense, CashDirection.Out, 50m, Staff, "tea", "Supplies", idempotencyKey: "k1");
        var second = shift.RecordCash(
            CashTransactionType.Expense, CashDirection.Out, 50m, Staff, "tea", "Supplies", idempotencyKey: "k1");

        Assert.Same(first, second);
        Assert.Equal(450m, shift.ExpectedCash);
        Assert.Equal(2, shift.CashTransactions.Count);

        Assert.Throws<BusinessRuleValidationException>(() => shift.RecordCash(
            CashTransactionType.Expense, CashDirection.Out, 60m, Staff, "tea", "Supplies", idempotencyKey: "k1"));
    }

    [Fact]
    public async Task Handler_RecordsExpenseAndReturnsSnapshot()
    {
        var shift = NewShift(500m);
        var (handler, unitOfWork) = CreateHandler(shift);

        var result = await handler.RecordExpenseAsync(new RecordExpenseCommand(
            shift.Id, Staff, 75m, "Cleaning", "detergent", null));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(425m, result.Value!.ExpectedCashAfter);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Handler_RejectsAdvanceWithoutBeneficiaryAndExpenseWithoutCategory()
    {
        var shift = NewShift(500m);
        var (handler, unitOfWork) = CreateHandler(shift);

        var advance = await handler.RecordWithdrawalAsync(new RecordWithdrawalCommand(
            shift.Id, Staff, 100m, WithdrawalCategory.Advance, "advance", null, null));
        var noCategory = await handler.RecordExpenseAsync(new RecordExpenseCommand(
            shift.Id, Staff, 10m, " ", "x", null));

        Assert.False(advance.IsSuccess);
        Assert.False(noCategory.IsSuccess);
        Assert.Equal(0, unitOfWork.SaveCalls);
        Assert.Equal(500m, shift.ExpectedCash);
    }

    [Fact]
    public async Task Handler_AdvanceStoresBeneficiaryAndSummaryReconciles()
    {
        var shift = NewShift(500m);
        var beneficiary = Guid.NewGuid();
        var (handler, _) = CreateHandler(shift);

        await handler.RecordWithdrawalAsync(new RecordWithdrawalCommand(
            shift.Id, Staff, 100m, WithdrawalCategory.Advance, "advance", beneficiary, null));
        await handler.RecordExpenseAsync(new RecordExpenseCommand(
            shift.Id, Staff, 40m, "Cleaning", "soap", null));

        var summary = (await handler.GetSummaryAsync(shift.Id)).Value!;

        Assert.Equal(500m, summary.TotalIn);
        Assert.Equal(140m, summary.TotalOut);
        Assert.Equal(summary.TotalIn - summary.TotalOut, summary.ExpectedCash);
        Assert.Equal(360m, summary.ExpectedCash);
        Assert.Equal(beneficiary,
            shift.CashTransactions.Single(item => item.Type == CashTransactionType.Withdrawal).BeneficiaryUserId);
    }

    [Fact]
    public async Task Handler_UnknownShiftFails()
    {
        var (handler, _) = CreateHandler(null);
        var result = await handler.RecordExpenseAsync(new RecordExpenseCommand(
            Guid.NewGuid(), Staff, 10m, "Cleaning", "x", null));
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Persistence_AppendsLedgerEntriesToReloadedShift()
    {
        var options = new DbContextOptionsBuilder<GHOSTDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        Guid shiftId;
        await using (var setup = new GHOSTDbContext(options))
        {
            var day = new BusinessDay(new DateOnly(2026, 11, 21));
            var shift = day.OpenShift(Staff, 300m);
            shiftId = shift.Id;
            await setup.BusinessDays.AddAsync(day);
            // In-memory has no FK enforcement, so no Users row is needed here.
            await setup.SaveChangesAsync();
        }

        await using (var work = new GHOSTDbContext(options))
        {
            var handler = new ShiftCashHandler(new ShiftRepository(work), new UnitOfWork(work));
            var result = await handler.RecordExpenseAsync(new RecordExpenseCommand(
                shiftId, Staff, 50m, "Cleaning", "mop", "key-1"));
            Assert.True(result.IsSuccess, result.Error);
        }

        await using (var verify = new GHOSTDbContext(options))
        {
            var entries = await verify.CashTransactions.Where(item => item.ShiftId == shiftId).ToListAsync();
            Assert.Equal(2, entries.Count);
            Assert.Equal(250m, (await verify.Shifts.SingleAsync(item => item.Id == shiftId)).ExpectedCash);
        }
    }

    private static (ShiftCashHandler Handler, CountingUnitOfWork UnitOfWork) CreateHandler(Shift? shift)
    {
        var unitOfWork = new CountingUnitOfWork();
        return (new ShiftCashHandler(new SingleShiftRepository(shift), unitOfWork), unitOfWork);
    }

    private sealed class SingleShiftRepository(Shift? shift) : IShiftRepository
    {
        public Task<Shift?> GetWithCashAsync(Guid shiftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(shift is not null && shift.Id == shiftId ? shift : null);

        public Task<Shift?> GetAsync(Guid shiftId, CancellationToken cancellationToken = default) =>
            GetWithCashAsync(shiftId, cancellationToken);

        public Task<IReadOnlyList<Shift>> GetOpenShiftsForUserWithCashAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Shift>>(
                shift is not null && shift.StaffUserId == userId && shift.Status == ShiftStatus.Open ? [shift] : []);

        public Task<bool> HandoverExistsAsync(Guid fromShiftId, Guid toShiftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task AddHandoverAsync(ShiftHandover handover, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(1);
        }
    }
}
