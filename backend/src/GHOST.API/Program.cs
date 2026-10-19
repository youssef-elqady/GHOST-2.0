using System.Text.Json.Serialization;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.BusinessDays.CloseShift;
using GHOST.Application.BusinessDays.OpenBusinessDay;
using GHOST.Application.BusinessDays.OpenShift;
using GHOST.Application.Devices.CreateDevice;
using GHOST.Application.Sessions;
using GHOST.Application.Sessions.StartSession;
using GHOST.Infrastructure.Persistence;
using GHOST.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

builder.Services.AddDbContext<GHOSTDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.MigrationsAssembly(
            typeof(GHOSTDbContext).Assembly.FullName)));

builder.Services.AddScoped<IBusinessDayRepository, BusinessDayRepository>();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<OpenBusinessDayHandler>();
builder.Services.AddScoped<CreateDeviceHandler>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddScoped<StartSessionHandler>();
builder.Services.AddScoped<SessionLifecycleHandler>();
builder.Services.AddScoped<TransferSessionHandler>();
builder.Services.AddScoped<OpenShiftHandler>();
builder.Services.AddScoped<CloseShiftHandler>();

var app = builder.Build();

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
