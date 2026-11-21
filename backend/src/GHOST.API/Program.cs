using System.Text.Json.Serialization;
using GHOST.API.Authentication;
using GHOST.API.Configuration;
using GHOST.API.Security;
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

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("GhostFrontend", policy =>
        policy.WithOrigins(
                "http://localhost:3000",
                "http://127.0.0.1:3000")
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
builder.Services.AddScoped<SessionLifecycleHandler>();
builder.Services.AddScoped<TransferSessionHandler>();
builder.Services.AddScoped<IShiftRepository, ShiftRepository>();
builder.Services.AddScoped<ShiftCashHandler>();
builder.Services.AddScoped<OpenShiftHandler>();
builder.Services.AddScoped<CloseShiftHandler>();

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
