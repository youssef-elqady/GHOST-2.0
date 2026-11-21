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
        public Uow Uow = new();
        public Product Cola;
        public SaleHandler Handler;

        public World()
        {
            Shifts.Shift = new BusinessDay(new DateOnly(2026, 11, 21)).OpenShift(Staff, 100m);
            Cola = Product.CreateActive("Cola", null, null, "Drinks", "can", 20m, Admin);
            Cola.ApplyMovement(StockMovementType.Purchase, 10, Admin, "seed", unitCost: 12m);
            Products.Map[Cola.Id] = Cola;
            Handler = new SaleHandler(Sales, Products, Shifts, new NoSessions(), Uow, Audit);
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
        var discount = new DiscountRequest(DiscountType.Fixed, 10m, "regular customer");

        var first = await w.Handler.CheckoutCashAsync(w.Command("k-2", discount, admin: true));
        var second = await w.Handler.CheckoutCashAsync(w.Command("k-2", discount, admin: true));

        Assert.Equal(50m, first.Value!.Total);
        Assert.Equal(50m, second.Value!.Total);
        Assert.Equal(150m, w.Shifts.Shift.ExpectedCash);
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

        var first = await w.Handler.RefundAsync(sale.Id, Admin, "customer returned", true, w.Shifts.Shift.Id);
        var second = await w.Handler.RefundAsync(sale.Id, Admin, "again", true, w.Shifts.Shift.Id);

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal(10m, w.Cola.StockQuantity);
        Assert.Equal(100m, w.Shifts.Shift.ExpectedCash);
    }
}
