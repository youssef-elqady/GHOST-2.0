using System.Reflection;
using System.Security.Claims;
using GHOST.API.Authentication;
using GHOST.API.Controllers;
using GHOST.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GHOST.Tests.Security;

/// <summary>
/// Evaluates every controller action through the real authorization policies, the way ASP.NET does:
/// class-level and action-level [Authorize] attributes are ALL required. Expected access is a reviewed table.
/// This catches a class-level AdminOnly silently locking Staff out of an action marked StaffOrAdmin.
/// </summary>
public sealed class EndpointRoleMatrixTests
{
    private const string A = "A"; // Admin only
    private const string S = "S"; // Staff or Admin

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["AlertsController.Get"] = A,
        ["BackupsController.List"] = A, ["BackupsController.Run"] = A,
        ["BusinessDaysController.OpenBusinessDay"] = A, ["BusinessDaysController.CloseShift"] = S, ["BusinessDaysController.OpenShift"] = S,
        ["DevicesController.GetAll"] = S, ["DevicesController.Create"] = A, ["DevicesController.SeedDefaultDevices"] = A, ["DevicesController.UpdateRates"] = A,
        ["ProductsController.List"] = S, ["ProductsController.GetByBarcode"] = S, ["ProductsController.Create"] = S,
        ["ProductsController.SetPrice"] = A, ["ProductsController.Enable"] = A, ["ProductsController.Disable"] = A,
        ["ProductsController.Movements"] = S, ["ProductsController.AdjustStock"] = S, ["ProductsController.GenerateBarcode"] = S,
        ["ReportsController.Dashboard"] = A, ["ReportsController.Daily"] = A, ["ReportsController.Shifts"] = A,
        ["ReportsController.Employees"] = A, ["ReportsController.Devices"] = A,
        ["SalesController.Checkout"] = S, ["SalesController.List"] = S, ["SalesController.Get"] = S, ["SalesController.Print"] = S, ["SalesController.Refund"] = A,
        ["SessionsController.Start"] = S, ["SessionsController.Pause"] = S, ["SessionsController.Resume"] = S,
        ["SessionsController.Transfer"] = S, ["SessionsController.Complete"] = S, ["SessionsController.Cancel"] = S,
        ["SettingsController.List"] = A, ["SettingsController.Update"] = A,
        ["ShiftCashController.AcknowledgeHandover"] = S, ["ShiftCashController.GetSummary"] = S,
        ["ShiftCashController.RecordExpense"] = S, ["ShiftCashController.RecordWithdrawal"] = S,
        ["StockReceiptsController.Create"] = S, ["StockReceiptsController.List"] = S,
        ["StockReceiptsController.Approve"] = A, ["StockReceiptsController.Reject"] = A,
        ["StocktakesController.Start"] = S, ["StocktakesController.List"] = S, ["StocktakesController.Get"] = S,
        ["StocktakesController.Counts"] = S, ["StocktakesController.Submit"] = S,
        ["StocktakesController.Approve"] = A, ["StocktakesController.Reject"] = A,
        ["BusinessDayReadsController.Current"] = S, ["ShiftsController.Current"] = S, ["ShiftsController.Get"] = S, ["ShiftsController.List"] = A,
        ["OutageReviewController.List"] = A, ["OutageReviewController.Approve"] = A,
        ["SessionReadsController.List"] = S, ["SessionReadsController.Get"] = S,
        ["AuditLogsController.List"] = A, ["UsersController.List"] = A,
        ["AuthController.ChangePassword"] = "P", // restricted password-change token, checked separately
    };

    private static readonly HashSet<string> Anonymous = ["AuthController.Login", "AuthController.Refresh", "AuthController.Logout"];

    private static IAuthorizationService Service()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddGhostJwtAuthentication(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Jwt:Issuer"] = "i", ["Authentication:Jwt:Audience"] = "a",
            ["Authentication:Jwt:SigningKey"] = new string('k', 40), ["Authentication:Jwt:AccessTokenLifetimeMinutes"] = "10"
        }).Build());
        return builder.Services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Principal(string? role)
    {
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString("D")), new("token_use", "access") };
        if (role is not null) claims.Add(new Claim("role", role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer", "unique_name", "role"));
    }

    private static IEnumerable<(string Key, MethodInfo Method)> Actions() =>
        typeof(AuthController).Assembly.GetTypes()
            .Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(m => ($"{t.Name}.{m.Name}", m)));

    private static async Task<bool> Allowed(IAuthorizationService svc, MethodInfo m, string? role)
    {
        var policies = m.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>(true)
            .Concat(m.GetCustomAttributes<AuthorizeAttribute>(true))
            .Select(a => a.Policy!).ToList();
        if (policies.Count == 0) return false;
        var principal = Principal(role);
        foreach (var p in policies)
            if (!(await svc.AuthorizeAsync(principal, null, p)).Succeeded) return false;
        return true;
    }

    [Fact]
    public void Every_action_is_listed_in_the_reviewed_table()
    {
        var actual = Actions().Select(a => a.Key).ToHashSet();
        var listed = Expected.Keys.Concat(Anonymous).ToHashSet();
        Assert.Empty(actual.Except(listed));
        Assert.Empty(listed.Except(actual));
    }

    [Fact]
    public async Task Admin_Staff_and_NoRole_get_exactly_the_reviewed_access()
    {
        var svc = Service();
        var failures = new List<string>();
        foreach (var (key, method) in Actions())
        {
            if (Anonymous.Contains(key)) continue;
            var (admin, staff, none) = (await Allowed(svc, method, "Admin"), await Allowed(svc, method, "Staff"), await Allowed(svc, method, null));
            var (wantAdmin, wantStaff) = Expected[key] switch { A => (true, false), S => (true, true), _ => (admin, staff) };
            if (Expected[key] == "P") continue;
            if (admin != wantAdmin || staff != wantStaff || none) failures.Add($"{key}: admin={admin} staff={staff} anon={none}, expected admin={wantAdmin} staff={wantStaff}");
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
