using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.Inventory;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;

namespace GHOST.Tests;

/// <summary>Staff intake corrections: separate linked movement, mandatory reason, own intakes only, capped, never negative.</summary>
public sealed class StockIntakeCorrectionTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid OtherStaff = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private sealed class Store : IStockIntakeStore
    {
        public List<StockMovement> Movements = new();

        public Task<IntakeMovementInfo?> GetMovementAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Movements.Where(m => m.Id == id)
                .Select(m => new IntakeMovementInfo(m.Id, m.ProductId, m.Type, m.QtyDelta, m.UserId)).FirstOrDefault());

        public Task<decimal> GetCorrectedQuantityAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(-Movements.Where(m => m.Type == StockMovementType.IntakeCorrection && m.SourceId == id).Sum(m => m.QtyDelta));

        public Task<IReadOnlyList<IntakeReportRow>> GetIntakeReportAsync(DateTimeOffset f, DateTimeOffset t, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntakeReportRow>>(new List<IntakeReportRow>());
    }

    private sealed class Products : IProductRepository
    {
        public Product P = null!;
        public Task<Product?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Product?>(P);
        public Task<Product?> GetByBarcodeAsync(string b, CancellationToken ct = default) => Task.FromResult<Product?>(null);
        public Task<bool> BarcodeExistsAsync(string b, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<Product>> ListAsync(ProductStatus? s, string? q, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Product>>(new List<Product>());
        public Task<IReadOnlyList<StockMovement>> GetMovementsAsync(Guid id, int take, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StockMovement>>(new List<StockMovement>());
        public Task<IReadOnlyList<Product>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Product>>(new List<Product>());
        public Task AddAsync(Product p, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Audit : IAuditTrail
    {
        public List<string> Actions = new();
        public void Record(Guid actorUserId, string action, string entityType, string entityId, string? newValuesJson = null) => Actions.Add(action);
    }

    private sealed class Uow : IUnitOfWork
    {
        public int Saves;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.FromResult(1); }
    }

    private static (StockIntakeHandler Handler, Store Store, Products Products, Audit Audit, Uow Uow, StockMovement Intake) Arrange(decimal intakeQty = 10m)
    {
        var product = Product.CreateActive("Cola", null, null, "Drinks", "Can", 20m, Admin, 10m, 30m, true);
        var intake = product.ApplyMovement(StockMovementType.Purchase, intakeQty, Staff, "Stock receipt", null, 8m, "StockReceipt", Guid.NewGuid());
        var store = new Store();
        store.Movements.Add(intake);
        var products = new Products { P = product };
        var audit = new Audit();
        var uow = new Uow();
        return (new StockIntakeHandler(products, store, uow, audit), store, products, audit, uow, intake);
    }

    [Fact]
    public async Task Correction_is_a_new_linked_movement_and_original_is_untouched()
    {
        var w = Arrange();
        var result = await w.Handler.CorrectAsync(new CorrectIntakeCommand(w.Intake.Id, Staff, false, 3m, "typed 10 instead of 7", null));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(7m, w.Products.P.StockQuantity);
        var correction = w.Products.P.Movements.Last();
        Assert.Equal(StockMovementType.IntakeCorrection, correction.Type);
        Assert.Equal(w.Intake.Id, correction.SourceId);
        Assert.Equal(-3m, correction.QtyDelta);
        Assert.Equal(10m, w.Intake.QtyDelta);
        Assert.Contains("Stock.IntakeCorrected", w.Audit.Actions);
        Assert.Equal(1, w.Uow.Saves);
    }

    [Fact]
    public async Task Reason_is_mandatory()
    {
        var w = Arrange();
        var result = await w.Handler.CorrectAsync(new CorrectIntakeCommand(w.Intake.Id, Staff, false, 3m, "  ", null));
        Assert.False(result.IsSuccess);
        Assert.Equal(10m, w.Products.P.StockQuantity);
        Assert.Equal(0, w.Uow.Saves);
    }

    [Fact]
    public async Task Staff_cannot_correct_someone_elses_intake_but_admin_can()
    {
        var w = Arrange();
        var denied = await w.Handler.CorrectAsync(new CorrectIntakeCommand(w.Intake.Id, OtherStaff, false, 1m, "x", null));
        Assert.False(denied.IsSuccess);
        var allowed = await w.Handler.CorrectAsync(new CorrectIntakeCommand(w.Intake.Id, Admin, true, 1m, "admin fix", null));
        Assert.True(allowed.IsSuccess, allowed.Error);
    }

    [Fact]
    public async Task Total_corrections_cannot_exceed_the_original_quantity()
    {
        var w = Arrange();
        Assert.True((await w.Handler.CorrectAsync(new CorrectIntakeCommand(w.Intake.Id, Staff, false, 6m, "a", null))).IsSuccess);
        w.Store.Movements.Add(w.Products.P.Movements.Last());
        var tooMuch = await w.Handler.CorrectAsync(new CorrectIntakeCommand(w.Intake.Id, Staff, false, 5m, "b", null));
        Assert.False(tooMuch.IsSuccess);
        Assert.Equal(4m, w.Products.P.StockQuantity);
    }

    [Fact]
    public async Task Correction_never_takes_stock_below_zero()
    {
        var w = Arrange();
        w.Products.P.ApplyMovement(StockMovementType.Waste, 8m, Staff, "spoiled");
        var result = await w.Handler.CorrectAsync(new CorrectIntakeCommand(w.Intake.Id, Staff, false, 5m, "wrong qty", null));
        Assert.False(result.IsSuccess);
        Assert.Equal(2m, w.Products.P.StockQuantity);
    }

    [Fact]
    public async Task Only_an_intake_can_be_corrected()
    {
        var w = Arrange();
        var waste = w.Products.P.ApplyMovement(StockMovementType.Waste, 1m, Staff, "spoiled");
        w.Store.Movements.Add(waste);
        var result = await w.Handler.CorrectAsync(new CorrectIntakeCommand(waste.Id, Staff, false, 1m, "x", null));
        Assert.False(result.IsSuccess);
    }
}
