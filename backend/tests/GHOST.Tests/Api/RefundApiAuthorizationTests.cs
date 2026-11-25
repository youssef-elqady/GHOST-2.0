using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GHOST.API.Authentication;
using GHOST.Application.Sales;
using GHOST.Domain.Entities;
using GHOST.Domain.Enums;
using GHOST.Infrastructure.Identity;
using GHOST.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GHOST.Tests.Api;

/// <summary>
/// Boots the real API (Program, controllers, JWT bearer, session-token validator, authorization policies) in memory
/// and calls POST /api/sales/{id}/refund over HTTP. Only the database is swapped for the EF in-memory provider and
/// the two background services are removed. Nothing here touches SQL Server or any real database.
/// These tests prove the authorization pipeline and handler wiring; they do NOT prove SQL Server behavior.
/// </summary>
public sealed class RefundApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "refund-api-" + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=localhost;Database=GHOST_IT_API_UNUSED;Trusted_Connection=True;TrustServerCertificate=True");
        builder.UseSetting("Authentication:Jwt:Issuer", "ghost-test");
        builder.UseSetting("Authentication:Jwt:Audience", "ghost-test");
        builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
        builder.UseSetting("Authentication:Jwt:AccessTokenLifetimeMinutes", "30");
        builder.UseSetting("Logging:File:Directory", Path.Combine(Path.GetTempPath(), "ghost-api-test-logs"));

        builder.ConfigureServices(services =>
        {
            // Drop every registration that carries the SQL Server provider for GHOSTDbContext (options and EF 9+ configuration).
            foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<GHOSTDbContext>)
                                                  || d.ServiceType == typeof(DbContextOptions)
                                                  || (d.ServiceType.IsGenericType && d.ServiceType.GenericTypeArguments.Contains(typeof(GHOSTDbContext)))).ToList())
                services.Remove(d);
            foreach (var d in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(d);

            services.AddDbContext<GHOSTDbContext>(o => o
                .UseInMemoryDatabase(_dbName)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        });
    }
}

public sealed class RefundApiAuthorizationTests : IClassFixture<RefundApiFactory>
{
    private readonly RefundApiFactory _factory;

    public RefundApiAuthorizationTests(RefundApiFactory factory) => _factory = factory;

    private sealed record Actor(ApplicationUser User, string Token);
    private sealed record Scenario(Guid SaleId, Guid ProductId, Guid AdminShiftId, Guid StaffShiftId, Actor Admin, Actor Staff, Actor NoRole, Actor Inactive);

    private async Task<Scenario> SeedAsync(bool anyShiftOpen = true, bool secondShiftOpen = false)
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<GHOSTDbContext>();
        var jwt = sp.GetRequiredService<JwtTokenService>();

        Actor MakeActor(string prefix, string? role, bool active = true)
        {
            var name = prefix + "_" + Guid.NewGuid().ToString("N")[..8];
            var user = new ApplicationUser { UserName = name, NormalizedUserName = name.ToUpperInvariant(), FullName = name, IsActive = active };
            db.Users.Add(user);
            var family = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            db.RefreshTokens.Add(RefreshToken.Issue(user.Id, family, new string('a', 64), now, TimeSpan.FromDays(7), now.AddDays(30)));
            var token = jwt.CreateAccessToken(user, role is null ? [] : [role], family).Value;
            return new Actor(user, token);
        }

        var admin = MakeActor("adm", "Admin");
        var staff = MakeActor("stf", "Staff");
        var noRole = MakeActor("nor", null);
        var inactive = MakeActor("ina", "Admin", active: false);

        // The venue has one open shift. This test database is shared by all tests of the class, so close what earlier tests left open.
        foreach (var leftOpen in await db.Shifts.Where(x => x.Status == ShiftStatus.Open).ToListAsync())
            leftOpen.Close(leftOpen.ExpectedCash, null);
        await db.SaveChangesAsync();

        var offset = Random.Shared.Next(1, 100000);
        var day = new BusinessDay(new DateOnly(2100, 1, 1).AddDays(offset));
        var staffShift = day.OpenShift(staff.User.Id, 100m);
        Shift? adminShift = null; // the Admin has no shift of their own: the refund uses the open (staff) shift
        db.BusinessDays.Add(day);
        if (secondShiftOpen)
        {
            var day2 = new BusinessDay(new DateOnly(2100, 1, 1).AddDays(offset + 200000));
            day2.OpenShift(admin.User.Id, 50m);
            db.BusinessDays.Add(day2);
        }

        var product = Product.CreateActive("Cola", null, null, "Drinks", "can", 20m, admin.User.Id);
        product.ApplyMovement(StockMovementType.Purchase, 10, admin.User.Id, "seed", unitCost: 12m);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        // The real checkout uses a SQL Server sequence for the invoice number, so the sale is created with the same
        // domain method the handler calls (it moves stock and cash), then saved to the in-memory database.
        var sale = Sale.CheckoutCash(staffShift, staff.User.Id, "TEST-" + Guid.NewGuid().ToString("N")[..8],
            new[] { new Sale.ProductLine(product, 3) }, null, null, "api-" + Guid.NewGuid());
        db.Sales.Add(sale);
        if (!anyShiftOpen) staffShift.Close(staffShift.ExpectedCash, null);
        await db.SaveChangesAsync();

        return new Scenario(sale.Id, product.Id, adminShift?.Id ?? Guid.Empty, staffShift.Id, admin, staff, noRole, inactive);
    }

    private async Task<HttpResponseMessage> RefundAsync(Guid saleId, string? token, object body)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/refund") { Content = JsonContent.Create(body) };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private async Task<(decimal Stock, SaleStatus Status, decimal AdminCash, decimal StaffCash)> StateAsync(Scenario s)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GHOSTDbContext>();
        var stock = await db.Products.AsNoTracking().Where(p => p.Id == s.ProductId).Select(p => p.StockQuantity).SingleAsync();
        var status = await db.Sales.AsNoTracking().Where(x => x.Id == s.SaleId).Select(x => x.Status).SingleAsync();
        var adminCash = s.AdminShiftId == Guid.Empty ? 0m : await db.Shifts.AsNoTracking().Where(x => x.Id == s.AdminShiftId).Select(x => x.ExpectedCash).SingleAsync();
        var staffCash = await db.Shifts.AsNoTracking().Where(x => x.Id == s.StaffShiftId).Select(x => x.ExpectedCash).SingleAsync();
        return (stock, status, adminCash, staffCash);
    }

    [Fact]
    public async Task Staff_gets_403_and_nothing_changes()
    {
        var s = await SeedAsync();
        var before = await StateAsync(s);
        var response = await RefundAsync(s.SaleId, s.Staff.Token, new { reason = "staff tries", returnToStock = true });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await StateAsync(s));
    }

    [Fact]
    public async Task No_token_gets_401_and_nothing_changes()
    {
        var s = await SeedAsync();
        var before = await StateAsync(s);
        var response = await RefundAsync(s.SaleId, null, new { reason = "anonymous", returnToStock = true });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, await StateAsync(s));
    }

    [Fact]
    public async Task Authenticated_user_without_a_role_gets_403_and_nothing_changes()
    {
        var s = await SeedAsync();
        var before = await StateAsync(s);
        var response = await RefundAsync(s.SaleId, s.NoRole.Token, new { reason = "no role", returnToStock = true });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await StateAsync(s));
    }

    [Fact]
    public async Task Deactivated_admin_token_is_rejected_with_401_and_nothing_changes()
    {
        var s = await SeedAsync();
        var before = await StateAsync(s);
        var response = await RefundAsync(s.SaleId, s.Inactive.Token, new { reason = "disabled", returnToStock = true });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, await StateAsync(s));
    }

    [Fact]
    public async Task Admin_can_refund_from_the_open_staff_shift_with_stock_untouched_by_default()
    {
        var s = await SeedAsync();
        var response = await RefundAsync(s.SaleId, s.Admin.Token, new { reason = "customer returned" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await StateAsync(s);
        Assert.Equal(SaleStatus.Refunded, state.Status);
        Assert.Equal(7m, state.Stock);          // ReturnToStock omitted = false: stock not returned
        Assert.Equal(100m, state.StaffCash);    // 100 + 60 sale - 60 refund: cash leaves the shift open now
    }

    [Fact]
    public async Task Admin_with_explicit_restock_returns_stock()
    {
        var s = await SeedAsync();
        var response = await RefundAsync(s.SaleId, s.Admin.Token, new { reason = "checked, resalable", returnToStock = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10m, (await StateAsync(s)).Stock);
    }

    [Fact]
    public async Task Admin_cannot_name_a_shift_that_is_not_the_open_one()
    {
        var s = await SeedAsync();
        var before = await StateAsync(s);
        var response = await RefundAsync(s.SaleId, s.Admin.Token, new { reason = "tamper", returnToStock = true, cashShiftId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await StateAsync(s));
    }

    [Fact]
    public async Task Refund_with_no_open_shift_is_refused()
    {
        var s = await SeedAsync(anyShiftOpen: false);
        var before = await StateAsync(s);
        var response = await RefundAsync(s.SaleId, s.Admin.Token, new { reason = "no shift", returnToStock = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await StateAsync(s));
    }

    [Fact]
    public async Task Two_open_shifts_refuse_the_refund_and_nothing_changes()
    {
        var s = await SeedAsync(secondShiftOpen: true);
        var before = await StateAsync(s);
        var response = await RefundAsync(s.SaleId, s.Admin.Token, new { reason = "two shifts", returnToStock = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("more than one shift is open", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await StateAsync(s));
    }

    [Fact]
    public async Task Second_refund_over_http_is_rejected()
    {
        var s = await SeedAsync();
        Assert.Equal(HttpStatusCode.OK, (await RefundAsync(s.SaleId, s.Admin.Token, new { reason = "first", returnToStock = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RefundAsync(s.SaleId, s.Admin.Token, new { reason = "second", returnToStock = true })).StatusCode);
        var state = await StateAsync(s);
        Assert.Equal(10m, state.Stock);
        Assert.Equal(100m, state.StaffCash);
    }
}
