using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Common.Exceptions;
using GHOST.Application.Inventory;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;

namespace GHOST.Tests.Inventory;

/// <summary>Unit tests with in-memory fakes. They prove the handler logic only, not the unique index on SQL Server.</summary>
public sealed class StockReceiptIdempotencyTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private sealed class FakeReceipts : IStockReceiptRepository
    {
        public List<StockReceipt> Saved { get; } = new();
        public List<StockReceipt> Pending { get; } = new();
        public StockReceipt? RaceWinner { get; set; }
        public int KeyLookups { get; private set; }
        public Task<StockReceipt?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Saved.FirstOrDefault(r => r.Id == id));
        public Task<IReadOnlyList<StockReceipt>> ListAsync(StockReceiptStatus? status, Guid? createdById, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StockReceipt>>(Saved.ToList());
        public Task<StockReceipt?> GetByIdempotencyKeyAsync(Guid createdById, string key, CancellationToken ct = default)
        {
            KeyLookups++;
            // A race winner becomes visible only after the first lookup (the loser looked before the winner committed).
            var found = Saved.FirstOrDefault(r => r.CreatedById == createdById && r.IdempotencyKey == key)
                        ?? (KeyLookups > 1 ? RaceWinner : null);
            return Task.FromResult(found);
        }
        public Task AddAsync(StockReceipt receipt, CancellationToken ct = default) { Pending.Add(receipt); return Task.CompletedTask; }
    }

    private sealed class FakeUnitOfWork(FakeReceipts receipts, bool failWithConflict = false) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            if (failWithConflict) { receipts.Pending.Clear(); throw new PersistenceConflictException("conflict", null!); }
            receipts.Saved.AddRange(receipts.Pending); receipts.Pending.Clear();
            return Task.FromResult(1);
        }
    }

    private sealed class FakeShifts(Shift shift) : IShiftRepository
    {
        public Task<Shift?> GetWithCashAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Shift?>(shift);
        public Task<Shift?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Shift?>(shift);
        public Task<IReadOnlyList<Shift>> GetOpenShiftsWithCashAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Shift>>(new[] { shift });
        public Task<IReadOnlyList<Shift>> GetOpenShiftsForUserWithCashAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Shift>>(new[] { shift });
        public Task<bool> HandoverExistsAsync(Guid a, Guid b, CancellationToken ct = default) => Task.FromResult(false);
        public Task AddHandoverAsync(ShiftHandover h, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeProducts(params Product[] products) : IProductRepository
    {
        public Task<Product?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(products.FirstOrDefault(p => p.Id == id));
        public Task<Product?> GetByBarcodeAsync(string b, CancellationToken ct = default) => Task.FromResult<Product?>(null);
        public Task<bool> BarcodeExistsAsync(string b, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<Product>> ListAsync(ProductStatus? s, string? q, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Product>>(products);
        public Task<IReadOnlyList<StockMovement>> GetMovementsAsync(Guid id, int take, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StockMovement>>(Array.Empty<StockMovement>());
        public Task<IReadOnlyList<Product>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Product>>(products.Where(p => ids.Contains(p.Id)).ToList());
        public Task AddAsync(Product p, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoAudit : IAuditTrail
    {
        public void Record(Guid actorUserId, string action, string entityType, string entityId, string? newValuesJson = null) { }
    }

    private static (StockReceiptHandler Handler, FakeReceipts Receipts, Shift Shift, Product Cola) Build(bool conflictOnSave = false)
    {
        var shift = new BusinessDay(new DateOnly(2026, 11, 25)).OpenShift(Staff, 500m);
        var cola = Product.CreateActive("Cola", null, null, "Drinks", "can", 20m, Admin);
        var receipts = new FakeReceipts();
        var handler = new StockReceiptHandler(receipts, new FakeProducts(cola), new FakeShifts(shift), new FakeUnitOfWork(receipts, conflictOnSave), new NoAudit());
        return (handler, receipts, shift, cola);
    }

    private static CreateReceiptCommand Cmd(Shift shift, Product p, decimal qty, string? key, decimal cost = 12m)
        => new(shift.Id, Staff, "Supplier", new[] { new ReceiptLineInput(p.Id, qty, cost, null) }, "INV-1", null, key);

    [Fact]
    public async Task SameKeyTwice_ReturnsTheSameReceipt_StockAndCashMoveOnce()
    {
        var (handler, receipts, shift, cola) = Build();
        var first = await handler.CreateAsync(Cmd(shift, cola, 10, "key-1"));
        var second = await handler.CreateAsync(Cmd(shift, cola, 10, "key-1"));

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Single(receipts.Saved);
        Assert.Equal(10m, cola.StockQuantity);
        Assert.Equal(380m, shift.ExpectedCash);
    }

    [Fact]
    public async Task KeyLongerThanColumn_IsRejectedCleanly_NothingMoves()
    {
        var (handler, receipts, shift, cola) = Build();
        var result = await handler.CreateAsync(Cmd(shift, cola, 10, new string('k', 101)));

        Assert.False(result.IsSuccess);
        Assert.Empty(receipts.Saved);
        Assert.Equal(0m, cola.StockQuantity);
    }

    [Fact]
    public async Task KeyOfExactlyMaxLength_AcceptedEvenWithSurroundingSpaces()
    {
        var (handler, receipts, shift, cola) = Build();
        var result = await handler.CreateAsync(Cmd(shift, cola, 10, " " + new string('k', 100) + " "));

        Assert.True(result.IsSuccess);
        Assert.Single(receipts.Saved);
    }

    [Fact]
    public async Task SameKeyDifferentQuantity_IsRejected_NothingExtraMoves()
    {
        var (handler, receipts, shift, cola) = Build();
        Assert.True((await handler.CreateAsync(Cmd(shift, cola, 10, "key-2"))).IsSuccess);
        var other = await handler.CreateAsync(Cmd(shift, cola, 11, "key-2"));

        Assert.False(other.IsSuccess);
        Assert.Single(receipts.Saved);
        Assert.Equal(10m, cola.StockQuantity);
        Assert.Equal(380m, shift.ExpectedCash);
    }

    [Fact]
    public async Task SameKeyDifferentCost_IsRejected()
    {
        var (handler, receipts, shift, cola) = Build();
        Assert.True((await handler.CreateAsync(Cmd(shift, cola, 10, "key-3", cost: 12m))).IsSuccess);
        Assert.False((await handler.CreateAsync(Cmd(shift, cola, 10, "key-3", cost: 13m))).IsSuccess);
        Assert.Single(receipts.Saved);
    }

    [Fact]
    public async Task DifferentKeys_AreTwoReceipts()
    {
        var (handler, receipts, shift, cola) = Build();
        Assert.True((await handler.CreateAsync(Cmd(shift, cola, 5, "key-a"))).IsSuccess);
        Assert.True((await handler.CreateAsync(Cmd(shift, cola, 5, "key-b"))).IsSuccess);
        Assert.Equal(2, receipts.Saved.Count);
        Assert.Equal(10m, cola.StockQuantity);
    }

    [Fact]
    public async Task LoserOfARace_GetsTheWinnersReceipt_InsteadOfAnError()
    {
        var (handler, receipts, shift, cola) = Build(conflictOnSave: true);
        // The winner is a receipt created by another request with the same key and content.
        var winnerShift = new BusinessDay(new DateOnly(2026, 11, 26)).OpenShift(Staff, 500m);
        var winnerCola = Product.CreateActive("Cola", null, null, "Drinks", "can", 20m, Admin);
        var winnerCmd = Cmd(shift, cola, 10, "key-race");
        var winner = StockReceipt.Receive(shift, Staff, "Supplier", new[] { new StockReceipt.Line(cola, 10, 12m, null) }, "INV-1", null,
            "key-race", StockReceiptHandler.Fingerprint(winnerCmd));
        receipts.RaceWinner = winner;
        _ = winnerShift; _ = winnerCola;

        var result = await handler.CreateAsync(Cmd(shift, cola, 10, "key-race"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(winner.Id, result.Value!.Id);
    }

    [Fact]
    public async Task RaceWithDifferentContent_IsAnError()
    {
        var (handler, receipts, shift, cola) = Build(conflictOnSave: true);
        var other = StockReceipt.Receive(shift, Staff, "Supplier", new[] { new StockReceipt.Line(cola, 20, 12m, null) }, "INV-1", null,
            "key-race2", StockReceiptHandler.Fingerprint(Cmd(shift, cola, 20, "key-race2")));
        receipts.RaceWinner = other;

        var result = await handler.CreateAsync(Cmd(shift, cola, 10, "key-race2"));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Fingerprint_IgnoresLineOrder_AndChangesWithContent()
    {
        var shift = new BusinessDay(new DateOnly(2026, 11, 25)).OpenShift(Staff, 500m);
        var a = Product.CreateActive("A", null, null, "D", "c", 1m, Admin);
        var b = Product.CreateActive("B", null, null, "D", "c", 1m, Admin);
        CreateReceiptCommand Make(params ReceiptLineInput[] lines) => new(shift.Id, Staff, "S", lines, "I", null, "k");
        var ab = StockReceiptHandler.Fingerprint(Make(new(a.Id, 1, 2m, null), new(b.Id, 3, 4m, null)));
        var ba = StockReceiptHandler.Fingerprint(Make(new(b.Id, 3, 4m, null), new(a.Id, 1, 2m, null)));
        var changed = StockReceiptHandler.Fingerprint(Make(new(a.Id, 1, 2m, null), new(b.Id, 3, 5m, null)));
        Assert.Equal(ab, ba);
        Assert.NotEqual(ab, changed);
    }
}
