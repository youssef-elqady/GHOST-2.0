using Serilog;
using System.Text.Json.Serialization;
using GHOST.API.Authentication;
using GHOST.API.Configuration;
using GHOST.API.Security;
using GHOST.Application.Abstractions.Auditing;
using GHOST.Application.Abstractions.CurrentUser;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.BusinessDays.CloseShift;
using GHOST.Application.Cash;
using GHOST.Application.BusinessDays.OpenBusinessDay;
using GHOST.Application.BusinessDays.OpenShift;
using GHOST.Application.Devices.CreateDevice;
using GHOST.Application.Sessions;
using GHOST.Application.Sessions.StartSession;
using GHOST.Infrastructure.Identity;
using GHOST.Infrastructure.Persistence;
using GHOST.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// One venue PC: listen on loopback only, run as a Windows Service when installed as one, log to rolling files.
GHOST.API.Configuration.LocalBindingPolicy.Validate(builder.Configuration["urls"] ?? builder.Configuration["ASPNETCORE_URLS"]);
builder.Host.UseWindowsService(options => options.ServiceName = "GHOST API");
builder.Host.UseSerilog((context, services, logger) => logger
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(context.Configuration["Logging:File:Directory"] ?? Path.Combine(AppContext.BaseDirectory, "logs"), "ghost-api-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        shared: false,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}"));

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

var allowedOrigins = GHOST.API.Authentication.LocalOriginPolicy.Validate(
    builder.Configuration.GetSection("Authentication:RefreshToken:AllowedOrigins").Get<string[]>()
    ?? new GHOST.API.Authentication.RefreshTokenOptions().AllowedOrigins);

builder.Services.AddCors(options =>
{
    options.AddPolicy("GhostFrontend", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

builder.Services.AddGhostDatabaseConnection(builder.Configuration);

builder.Services.AddDbContext<GHOSTDbContext>((serviceProvider, options) =>
{
    var databaseOptions = serviceProvider
        .GetRequiredService<IOptions<DatabaseConnectionOptions>>()
        .Value;

    options.UseSqlServer(
        databaseOptions.DefaultConnection,
        sqlOptions => sqlOptions.MigrationsAssembly(
            typeof(GHOSTDbContext).Assembly.FullName));
});

builder.Services.AddGhostIdentity();
builder.Services.AddGhostJwtAuthentication(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddScoped<ShiftAccess>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddOptions<RefreshTokenOptions>()
    .Bind(builder.Configuration.GetSection(RefreshTokenOptions.SectionName));
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<IAuthenticationAuditWriter, AuthenticationAuditWriter>();

builder.Services.AddScoped<IBusinessDayRepository, BusinessDayRepository>();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<OpenBusinessDayHandler>();
builder.Services.AddScoped<CreateDeviceHandler>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddScoped<StartSessionHandler>();
builder.Services.AddSingleton(
    builder.Configuration.GetSection(SessionPolicy.SectionName).Get<SessionPolicy>()
    ?? new SessionPolicy());
builder.Services.AddScoped<IAuditTrail, AuditTrail>();
builder.Services.AddSingleton<GHOST.Application.Abstractions.Auditing.IRejectionAudit, GHOST.Infrastructure.Persistence.RejectionAudit>();
builder.Services.AddScoped<CancelSessionHandler>();
builder.Services.AddScoped<SessionLifecycleHandler>();
builder.Services.AddScoped<TransferSessionHandler>();
builder.Services.AddScoped<IShiftRepository, ShiftRepository>();
builder.Services.AddScoped<ShiftCashHandler>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ISaleRepository, SaleRepository>();
builder.Services.AddScoped<IStocktakeRepository, StocktakeRepository>();
builder.Services.AddScoped<GHOST.Application.Inventory.StocktakeHandler>();
builder.Services.AddScoped<GHOST.Application.Reports.IReportQueries, GHOST.Infrastructure.Persistence.ReportQueries>();
builder.Services.AddScoped<GHOST.Application.Sales.SaleHandler>();
builder.Services.AddScoped<IStockReceiptRepository, StockReceiptRepository>();
builder.Services.AddScoped<GHOST.Application.Inventory.StockReceiptHandler>();
builder.Services.AddScoped<GHOST.Application.Inventory.InventoryHandler>();
builder.Services.AddScoped<GHOST.Application.Inventory.IStockIntakeStore, GHOST.Infrastructure.Persistence.Repositories.StockIntakeStore>();
builder.Services.AddScoped<GHOST.Application.Inventory.StockIntakeHandler>();
builder.Services.AddScoped<AcknowledgeHandoverHandler>();
builder.Services.AddScoped<OpenShiftHandler>();
builder.Services.AddScoped<CloseShiftHandler>();

builder.Services.AddScoped<ISettingsRepository, GHOST.Infrastructure.Persistence.Repositories.SettingsRepository>();
builder.Services.AddScoped<GHOST.Application.Settings.SettingsHandler>();
builder.Services.AddScoped<GHOST.Application.Alerts.IAlertQueries, GHOST.Infrastructure.Persistence.AlertQueries>();
builder.Services.AddScoped<GHOST.Application.Alerts.AlertsHandler>();
builder.Services.AddScoped<GHOST.Application.Reads.IOperationsReadQueries, GHOST.Infrastructure.Persistence.OperationsReadQueries>();
builder.Services.AddScoped<GHOST.Application.Reads.OperationsReadHandler>();
builder.Services.Configure<GHOST.API.Backup.ScheduledBackupOptions>(
    builder.Configuration.GetSection(GHOST.API.Backup.ScheduledBackupOptions.SectionName));
builder.Services.AddScoped<IBackupRepository, GHOST.Infrastructure.Persistence.Repositories.BackupRepository>();
builder.Services.AddScoped<GHOST.Application.Backup.IDatabaseBackupGateway, GHOST.Infrastructure.Persistence.SqlServerBackupGateway>();
builder.Services.AddScoped(sp =>
{
    var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GHOST.API.Backup.ScheduledBackupOptions>>().Value;
    return new GHOST.Application.Backup.BackupOptions(o.Directory ?? string.Empty, Math.Clamp(o.RetentionCount, 1, 60), o.ExternalDirectory, o.ExternalDriveMarker, o.ExternalDriveId);
});
builder.Services.AddSingleton<GHOST.Application.Backup.IExternalDriveGuard, GHOST.Infrastructure.Persistence.ExternalDriveGuard>();
builder.Services.AddScoped<GHOST.Application.Backup.BackupHandler>();
builder.Services.AddHostedService<GHOST.API.Backup.ScheduledBackupService>();
builder.Services.Configure<GHOST.API.Outage.OutageOptions>(
    builder.Configuration.GetSection(GHOST.API.Outage.OutageOptions.SectionName));
builder.Services.AddScoped<GHOST.Application.Abstractions.Persistence.IOutageRepository, GHOST.Infrastructure.Persistence.Repositories.OutageRepository>();
builder.Services.AddScoped<GHOST.Application.Abstractions.Auditing.ISystemAuditTrail, GHOST.Infrastructure.Persistence.SystemAuditTrail>();
builder.Services.AddScoped<GHOST.Application.Sessions.PowerOutageHandler>();
builder.Services.AddHostedService<GHOST.API.Outage.OutageRecoveryService>();
builder.Services.AddSingleton<GHOST.API.Backup.ShiftCloseBackupTrigger>();

var app = builder.Build();

app.UseCors("GhostFrontend");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();
app.MapGet("/", () => "GHOST 2.0 API is running");

// Liveness: the process answers. Readiness: database reachable AND every migration applied. Anonymous, local only,
// and it never returns connection details.
app.MapGet("/health/live", () => Results.Ok(new { status = "Live" })).AllowAnonymous();
app.MapGet("/health/ready", async (GHOSTDbContext db, CancellationToken ct) =>
{
    var databaseReachable = false;
    var migrationsApplied = false;
    try
    {
        databaseReachable = await db.Database.CanConnectAsync(ct);
        migrationsApplied = databaseReachable && !(await db.Database.GetPendingMigrationsAsync(ct)).Any();
    }
    catch (Exception)
    {
        // Reported as not ready below; the reason is in the log, never in the response.
    }

    var ready = databaseReachable && migrationsApplied;
    return Results.Json(new { status = ready ? "Ready" : "NotReady", databaseReachable, migrationsApplied },
        statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapGet("/health/database", async (
    GHOSTDbContext db,
    CancellationToken cancellationToken) =>
{
    var connected = await db.Database.CanConnectAsync(cancellationToken);

    if (!connected)
    {
        return Results.Problem(
            title: "Database unavailable",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new
    {
        status = "Healthy",
        database = "Connected"
    });
});

app.Run();
