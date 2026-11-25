using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Sales;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;

namespace GHOST.Tests.Sales;

/// <summary>Proof for: repeating a checkout never charges, discounts or decrements stock twice.</summary>
public sealed class SaleHandlerIdempotencyTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private sealed class Sales : ISaleRepository
    {
        public List<Sale> Rows = new();
        public int Counter;

        public Task<Sale?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(s => s.Id == id));

        public Task<Sale?> GetByIdempotencyKeyAsync(string key, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(s => s.IdempotencyKey == key));

        public Task<bool> SessionIsInvoicedAsync(Guid sessionId, CancellationToken ct = default) => Task.FromResult(false);

        public Task<string> NextInvoiceNumberAsync(DateTimeOffset now, CancellationToken ct = default)
            => Task.FromResult($"GS-2026-{++Counter:D5}");

        public Task<IReadOnlyList<Sale>> ListAsync(Guid? shiftId, SaleStatus? status, Guid? createdById, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Sale>>(Rows);

        public Task AddAsync(Sale sale, CancellationToken ct = default)
        {
            Rows.Add(sale);
            return Task.CompletedTask;
        }
    }

    private sealed class Products : IProductRepository
    {
        public Dictionary<Guid, Product> Map = new();

        public Task<IReadOnlyList<Product>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Product>>(ids.Where(Map.ContainsKey).Select(i => Map[i]).ToList());

        public Task<Product?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Map.GetValueOrDefault(id));
        public Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default) => Task.FromResult<Product?>(null);
        public Task<bool> BarcodeExistsAsync(string barcode, CancellationToken ct = default) => Task.FromResult(false);

        public Task<IReadOnlyList<Product>> ListAsync(ProductStatus? status, string? search, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Product>>(Map.Values.ToList());

        public Task<IReadOnlyList<StockMovement>> GetMovementsAsync(Guid productId, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StockMovement>>(new List<StockMovement>());

        public Task AddAsync(Product product, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Shifts : IShiftRepository
    {
        public Shift Shift = null!;
        public Dictionary<Guid, Shift> OtherOpenShifts = new();

        public List<Shift> ExtraAdminShifts = new();

        public Task<IReadOnlyList<Shift>> GetOpenShiftsWithCashAsync(CancellationToken ct = default)
        {
            var list = new List<Shift>();
            if (Shift.Status == ShiftStatus.Open) list.Add(Shift);
            list.AddRange(ExtraAdminShifts.Where(s => s.Status == ShiftStatus.Open));
            return Task.FromResult<IReadOnlyList<Shift>>(list);
        }

        public Task<IReadOnlyList<Shift>> GetOpenShiftsForUserWithCashAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Shift>>(new List<Shift>());

        public Task<Shift?> GetWithCashAsync(Guid shiftId, CancellationToken ct = default) => Task.FromResult<Shift?>(Shift);
        public Task<Shift?> GetAsync(Guid shiftId, CancellationToken ct = default) => Task.FromResult<Shift?>(Shift);
        public Task<bool> HandoverExistsAsync(Guid f, Guid t, CancellationToken ct = default) => Task.FromResult(false);
        public Task AddHandoverAsync(ShiftHandover handover, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoSessions : ISessionRepository
    {
        public Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Session?>(null);
        public Task<Session?> GetActiveByDeviceAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Session?>(null);
        public Task<Device?> GetDeviceForStartAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Device?>(null);
        public Task<Shift?> GetShiftAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Shift?>(null);
        public Task AddAsync(Session session, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Audit : IAuditTrail
    {
        public List<string> Actions = new();

        public void Record(Guid actorUserId, string action, string entityType, string entityId, string? newValuesJson = null)
            => Actions.Add(action);
    }

    private sealed class Rejections : IRejectionAudit
    {
        public List<string> Actions = new();
        public List<(string Action, string? EntityId, string Reason)> Records = new();
        public Task RecordAsync(Guid? actorUserId, string action, string entityType, string? entityId, string reason, CancellationToken ct = default)
        { Actions.Add(action); Records.Add((action, entityId, reason)); return Task.CompletedTask; }
    }

    private sealed class Uow : IUnitOfWork
    {
        public int Saves;
        public Action? OnSave;

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            OnSave?.Invoke();
            return Task.FromResult(1);
        }
    }

    private sealed class World
    {
        public Sales Sales = new();
        public Products Products = new();
        public Shifts Shifts = new();
        public Audit Audit = new();
        public Rejections Rejections = new();
        public Uow Uow = new();
        public Product Cola;
        public SaleHandler Handler;

        public World()
        {
            var day = new BusinessDay(new DateOnly(2026, 11, 21));
            Shifts.Shift = day.OpenShift(Staff, 100m);
            Cola = Product.CreateActive("Cola", null, null, "Drinks", "can", 20m, Admin);
            Cola.ApplyMovement(StockMovementType.Purchase, 10, Admin, "seed", unitCost: 12m);
            Products.Map[Cola.Id] = Cola;
            Handler = new SaleHandler(Sales, Products, Shifts, new NoSessions(), Uow, Audit, Rejections);
        }

        public CheckoutCommand Command(string? key, DiscountRequest? discount = null, bool admin = false) => new(
            Shifts.Shift.Id, admin ? Admin : Staff, admin,
            new[] { new SaleLineInput(Cola.Id, 3) }, null, discount, key);
    }

    [Fact]
    public async Task SameKeyTwice_ReturnsSameInvoice_ChargesAndDecrementsOnce()
    {
        var w = new World();

        var first = await w.Handler.CheckoutCashAsync(w.Command("k-1"));
        var second = await w.Handler.CheckoutCashAsync(w.Command("k-1"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Single(w.Sales.Rows);
        Assert.Equal(7m, w.Cola.StockQuantity);
        Assert.Equal(160m, w.Shifts.Shift.ExpectedCash);
        Assert.Equal(1, w.Uow.Saves);
        Assert.Single(w.Audit.Actions);
    }

    [Fact]
    public async Task SameKeyTwice_WithDiscount_AppliesDiscountOnce()
    {
        var w = new World();
        var discount = new DiscountRequest(DiscountType.Fixed, 6m, "regular customer");

        var first = await w.Handler.CheckoutCashAsync(w.Command("k-2", discount, admin: true));
        var second = await w.Handler.CheckoutCashAsync(w.Command("k-2", discount, admin: true));

        Assert.Equal(54m, first.Value!.Total);
        Assert.Equal(54m, second.Value!.Total);
        Assert.Equal(154m, w.Shifts.Shift.ExpectedCash);
        Assert.Single(w.Sales.Rows);
    }

    [Fact]
    public async Task TwoSeparatePurchases_WithIdenticalCart_AndDifferentKeys_BothGoThrough()
    {
        var w = new World();

        var first = await w.Handler.CheckoutCashAsync(w.Command("buy-1"));
        var second = await w.Handler.CheckoutCashAsync(w.Command("buy-2"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value!.Id, second.Value!.Id);
        Assert.Equal(2, w.Sales.Rows.Count);
        Assert.Equal(4m, w.Cola.StockQuantity);
        Assert.Equal(220m, w.Shifts.Shift.ExpectedCash);
    }

    [Fact]
    public async Task SameKey_ForDifferentCartContent_IsRejected_AndChargesNothingExtra()
    {
        var w = new World();
        await w.Handler.CheckoutCashAsync(w.Command("k-9"));

        var different = new CheckoutCommand(w.Shifts.Shift.Id, Staff, false,
            new[] { new SaleLineInput(w.Cola.Id, 5) }, null, null, "k-9");
        var result = await w.Handler.CheckoutCashAsync(different);

        Assert.False(result.IsSuccess);
        Assert.Single(w.Sales.Rows);
        Assert.Equal(7m, w.Cola.StockQuantity);
    }

    [Fact]
    public async Task KeyUsedByAnotherStaff_IsRejected_NotReplayed()
    {
        var w = new World();
        await w.Handler.CheckoutCashAsync(w.Command("k-3"));

        var other = new CheckoutCommand(w.Shifts.Shift.Id, Guid.NewGuid(), false,
            new[] { new SaleLineInput(w.Cola.Id, 1) }, null, null, "k-3");
        var result = await w.Handler.CheckoutCashAsync(other);

        Assert.False(result.IsSuccess);
        Assert.Single(w.Sales.Rows);
    }

    [Fact]
    public async Task ConcurrentDuplicate_LosingRequest_GetsTheWinnersInvoice()
    {
        // Simulates two identical requests in flight: the winner commits between our key check and our save,
        // so our save hits the unique index on the idempotency key.
        var w = new World();
        var winner = Sale.CheckoutCash(w.Shifts.Shift, Staff, "GS-WINNER",
            new[] { new Sale.ProductLine(w.Cola, 3) }, null, null, "k-4");
        w.Uow.OnSave = () =>
        {
            w.Sales.Rows.Remove(w.Sales.Rows.Last());
            w.Sales.Rows.Add(winner);
            throw new PersistenceConflictException("unique");
        };

        var result = await w.Handler.CheckoutCashAsync(w.Command("k-4"));

        Assert.True(result.IsSuccess);
        Assert.Equal(winner.Id, result.Value!.Id);
    }

    [Fact]
    public async Task ConcurrentConflictWithoutWinner_ReportsRetryMessage()
    {
        var w = new World();
        w.Uow.OnSave = () =>
        {
            w.Sales.Rows.Remove(w.Sales.Rows.Last()); // the failed insert never reaches the database
            throw new PersistenceConflictException("stock changed");
        };

        var result = await w.Handler.CheckoutCashAsync(w.Command("k-5"));

        Assert.False(result.IsSuccess);
        Assert.Contains("try again", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SecondRefund_IsRejected_NoSecondCashOutOrStockReturn()
    {
        var w = new World();
        var sale = (await w.Handler.CheckoutCashAsync(w.Command("k-6"))).Value!;

        var first = await w.Handler.RefundAsync(sale.Id, Admin, "customer returned", true, null);
        var second = await w.Handler.RefundAsync(sale.Id, Admin, "again", true, null);

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal(10m, w.Cola.StockQuantity);
        Assert.Equal(100m, w.Shifts.Shift.ExpectedCash); // 100 + 60 sale - 60 refund, once
    }

    [Fact]
    public async Task Refund_posts_to_the_shift_open_now_whoever_owns_it_with_the_admin_as_approver()
    {
        var w = new World();
        var sale = (await w.Handler.CheckoutCashAsync(w.Command("k-7"))).Value!;
        var result = await w.Handler.RefundAsync(sale.Id, Admin, "returned", false, null);
        Assert.True(result.IsSuccess);
        Assert.Equal(100m, w.Shifts.Shift.ExpectedCash); // the staff shift is the one open: 160 - 60
        Assert.Equal(Admin, Assert.Single((await w.Sales.GetAsync(sale.Id))!.Refunds).ApprovedById);
    }

    [Fact]
    public async Task Refund_with_someone_elses_shift_id_is_rejected_and_changes_nothing()
    {
        var w = new World();
        var sale = (await w.Handler.CheckoutCashAsync(w.Command("k-8"))).Value!;
        var result = await w.Handler.RefundAsync(sale.Id, Admin, "returned", true, Guid.NewGuid()); // not the open shift
        Assert.False(result.IsSuccess);
        Assert.Contains("not allowed", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7m, w.Cola.StockQuantity);
        Assert.Equal(160m, w.Shifts.Shift.ExpectedCash);
        Assert.Contains("Sale.RefundRejected", w.Rejections.Actions);
    }

    [Fact]
    public async Task Refund_with_no_open_shift_is_blocked_before_any_change()
    {
        var w = new World();
        var sale = (await w.Handler.CheckoutCashAsync(w.Command("k-9"))).Value!;
        w.Shifts.Shift.Close(w.Shifts.Shift.ExpectedCash, null);
        var result = await w.Handler.RefundAsync(sale.Id, Admin, "returned", true, null);
        Assert.False(result.IsSuccess);
        Assert.Equal(7m, w.Cola.StockQuantity);
        Assert.Equal(160m, w.Shifts.Shift.ExpectedCash);
    }

    [Fact]
    public async Task Refund_without_restock_leaves_stock_alone_and_with_explicit_restock_returns_it()
    {
        var a = new World();
        var saleA = (await a.Handler.CheckoutCashAsync(a.Command("k-11"))).Value!;
        Assert.True((await a.Handler.RefundAsync(saleA.Id, Admin, "damaged goods", false, null)).IsSuccess);
        Assert.Equal(7m, a.Cola.StockQuantity);

        var b = new World();
        var saleB = (await b.Handler.CheckoutCashAsync(b.Command("k-12"))).Value!;
        Assert.True((await b.Handler.RefundAsync(saleB.Id, Admin, "checked and resalable", true, null)).IsSuccess);
        Assert.Equal(10m, b.Cola.StockQuantity);
    }

    [Fact]
    public async Task Refund_is_rejected_when_more_than_one_shift_is_open_and_nothing_changes()
    {
        var w = new World();
        var sale = (await w.Handler.CheckoutCashAsync(w.Command("k-13"))).Value!;
        w.Shifts.ExtraAdminShifts.Add(new BusinessDay(new DateOnly(2026, 11, 22)).OpenShift(Admin, 50m));

        var result = await w.Handler.RefundAsync(sale.Id, Admin, "returned", true, null);

        Assert.False(result.IsSuccess);
        Assert.Contains("more than one shift is open", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7m, w.Cola.StockQuantity);
        Assert.Equal(160m, w.Shifts.Shift.ExpectedCash);
        Assert.Equal(50m, w.Shifts.ExtraAdminShifts[0].ExpectedCash);
        Assert.Contains("Sale.RefundRejected", w.Rejections.Actions);
    }

    [Fact]
    public async Task SameKey_SameDiscount_DifferentReason_IsRejected_AndChargesNothingExtra()
    {
        var w = new World();
        var first = await w.Handler.CheckoutCashAsync(w.Command("k-b10", new DiscountRequest(DiscountType.Fixed, 6m, "regular customer"), admin: true));
        Assert.True(first.IsSuccess, first.Error);

        var second = await w.Handler.CheckoutCashAsync(w.Command("k-b10", new DiscountRequest(DiscountType.Fixed, 6m, "owner friend"), admin: true));

        Assert.False(second.IsSuccess);
        Assert.Single(w.Sales.Rows);
        Assert.Equal(7m, w.Cola.StockQuantity);
    }

    [Fact]
    public async Task SameKey_SameDiscount_ReasonWithOnlyOuterSpaces_IsTheSameRequest()
    {
        var w = new World();
        var first = await w.Handler.CheckoutCashAsync(w.Command("k-b10b", new DiscountRequest(DiscountType.Fixed, 6m, "regular customer"), admin: true));
        var again = await w.Handler.CheckoutCashAsync(w.Command("k-b10b", new DiscountRequest(DiscountType.Fixed, 6m, "  regular customer "), admin: true));

        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal(first.Value!.Id, again.Value!.Id);
        Assert.Single(w.Sales.Rows);
    }

    private sealed class FixedSettings(params (string Key, string Value)[] rows) : ISettingsRepository
    {
        public Task<IReadOnlyList<SystemSetting>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SystemSetting>>(rows.Select(r => new SystemSetting(r.Key, r.Value, Admin, DateTimeOffset.UtcNow)).ToList());
        public Task<SystemSetting?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult(rows.Where(r => r.Key == key).Select(r => new SystemSetting(r.Key, r.Value, Admin, DateTimeOffset.UtcNow)).FirstOrDefault());
        public Task AddAsync(SystemSetting setting, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static SaleHandler HandlerWith(World w, ISettingsRepository settings)
        => new(w.Sales, w.Products, w.Shifts, new NoSessions(), w.Uow, w.Audit, w.Rejections, settings);

    [Fact]
    public async Task AdminDiscount_AboveTheStoredCap_IsRefused_AndNothingChanges()
    {
        var w = new World();
        // No stored settings: the defaults apply (10% on products).
        var handler = HandlerWith(w, new FixedSettings());

        // Subtotal 60. 20% = 12 is above the 10% cap.
        var tooMuch = await handler.CheckoutCashAsync(w.Command("k-cap1", new DiscountRequest(DiscountType.Percent, 20m, "promo"), admin: true));
        var fine = await handler.CheckoutCashAsync(w.Command("k-cap2", new DiscountRequest(DiscountType.Percent, 10m, "promo"), admin: true));

        Assert.False(tooMuch.IsSuccess);
        Assert.Contains("above the maximum allowed", tooMuch.Error!);
        Assert.True(fine.IsSuccess, fine.Error);
        Assert.Single(w.Sales.Rows);
        Assert.Equal(7m, w.Cola.StockQuantity);
    }

    [Fact]
    public async Task HundredPercentDiscount_IsRefusedUnlessZeroTotalIsAllowedInSettings()
    {
        var w = new World();
        var caps = new (string, string)[]
        {
            (GHOST.Application.Settings.SettingsCatalog.MaxProductDiscountPercent, "100"),
            (GHOST.Application.Settings.SettingsCatalog.MaxGamingDiscountPercent, "100"),
        };
        var refused = await HandlerWith(w, new FixedSettings(caps)).CheckoutCashAsync(w.Command("k-zero1", new DiscountRequest(DiscountType.Percent, 100m, "gift"), admin: true));
        Assert.False(refused.IsSuccess);
        Assert.Empty(w.Sales.Rows);
        Assert.Equal(10m, w.Cola.StockQuantity);

        var handler = HandlerWith(w, new FixedSettings(caps.Append((GHOST.Application.Settings.SettingsCatalog.AllowZeroTotalInvoice, "1")).ToArray()));
        var allowed = await handler.CheckoutCashAsync(w.Command("k-zero2", new DiscountRequest(DiscountType.Percent, 100m, "gift"), admin: true));
        Assert.True(allowed.IsSuccess, allowed.Error);
        Assert.Equal(0m, allowed.Value!.Total);
    }

    // ---- invoice number: gaps are allowed, duplicates and silent gaps are not ----

    [Fact]
    public async Task FailedCheckout_AfterTakingANumber_RecordsThatNumberAsUnused_WithTheReason()
    {
        var w = new World();
        var tooMany = new CheckoutCommand(w.Shifts.Shift.Id, Staff, false,
            new[] { new SaleLineInput(w.Cola.Id, 50) }, null, null, "fail-1");

        var result = await w.Handler.CheckoutCashAsync(tooMany);

        Assert.False(result.IsSuccess);
        Assert.Empty(w.Sales.Rows);
        Assert.Equal(1, w.Sales.Counter); // one number was taken and is not given back
        var note = Assert.Single(w.Rejections.Records, r => r.Action == InvoiceNumberGaps.UnusedNumberAction);
        Assert.Equal("GS-2026-00001", note.EntityId);
        Assert.False(string.IsNullOrWhiteSpace(note.Reason));
    }

    [Fact]
    public async Task NextSale_AfterAFailedOne_GetsANewNumber_NeverTheFailedOne()
    {
        var w = new World();
        var tooMany = new CheckoutCommand(w.Shifts.Shift.Id, Staff, false,
            new[] { new SaleLineInput(w.Cola.Id, 50) }, null, null, "fail-2");
        await w.Handler.CheckoutCashAsync(tooMany);

        var ok = await w.Handler.CheckoutCashAsync(w.Command("ok-2"));

        Assert.True(ok.IsSuccess);
        Assert.Equal("GS-2026-00002", ok.Value!.InvoiceNumber);
    }

    [Fact]
    public async Task RetryWithSameKey_AfterSuccess_TakesNoNewNumber_AndRecordsNoUnusedNote()
    {
        var w = new World();

        await w.Handler.CheckoutCashAsync(w.Command("retry-1"));
        await w.Handler.CheckoutCashAsync(w.Command("retry-1"));
        await w.Handler.CheckoutCashAsync(w.Command("retry-1"));

        Assert.Equal(1, w.Sales.Counter);
        Assert.Single(w.Sales.Rows);
        Assert.DoesNotContain(w.Rejections.Records, r => r.Action == InvoiceNumberGaps.UnusedNumberAction);
    }

    [Fact]
    public async Task ConcurrentDuplicate_Loser_RecordsItsNumberAsUnused_AndTheWinnerKeepsItsOwn()
    {
        var w = new World();
        var winner = Sale.CheckoutCash(w.Shifts.Shift, Staff, "GS-2026-00099",
            new[] { new Sale.ProductLine(w.Cola, 3) }, null, null, "dup-1");
        w.Uow.OnSave = () =>
        {
            w.Sales.Rows.Remove(w.Sales.Rows.Last());
            w.Sales.Rows.Add(winner);
            throw new PersistenceConflictException("unique");
        };

        var result = await w.Handler.CheckoutCashAsync(w.Command("dup-1"));

        Assert.True(result.IsSuccess);
        Assert.Equal("GS-2026-00099", result.Value!.InvoiceNumber);
        var note = Assert.Single(w.Rejections.Records, r => r.Action == InvoiceNumberGaps.UnusedNumberAction);
        Assert.Equal("GS-2026-00001", note.EntityId); // the loser's own number, not the winner's
        Assert.Contains("same idempotency key", note.Reason);
    }

    [Fact]
    public async Task ConflictWithoutWinner_RecordsTheUnusedNumber()
    {
        var w = new World();
        w.Uow.OnSave = () =>
        {
            w.Sales.Rows.Remove(w.Sales.Rows.Last());
            throw new PersistenceConflictException("stock changed");
        };

        var result = await w.Handler.CheckoutCashAsync(w.Command("conf-1"));

        Assert.False(result.IsSuccess);
        Assert.Single(w.Rejections.Records, r => r.Action == InvoiceNumberGaps.UnusedNumberAction && r.EntityId == "GS-2026-00001");
    }

    [Fact]
    public async Task RejectionBeforeANumberIsTaken_ConsumesNoNumber()
    {
        var w = new World();
        var unknownProduct = new CheckoutCommand(w.Shifts.Shift.Id, Staff, false,
            new[] { new SaleLineInput(Guid.NewGuid(), 1) }, null, null, "none-1");

        var result = await w.Handler.CheckoutCashAsync(unknownProduct);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, w.Sales.Counter);
        Assert.DoesNotContain(w.Rejections.Records, r => r.Action == InvoiceNumberGaps.UnusedNumberAction);
    }
}
