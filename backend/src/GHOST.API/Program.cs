using GHOST.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using GHOST.Application.Abstractions.Persistence;
using GHOST.Application.BusinessDays.OpenBusinessDay;
using GHOST.Infrastructure.Persistence.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<GHOSTDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.MigrationsAssembly(
            typeof(GHOSTDbContext).Assembly.FullName)));

builder.Services.AddScoped<IBusinessDayRepository, BusinessDayRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<OpenBusinessDayHandler>();

var app = builder.Build();

app.MapControllers();

app.MapGet("/", () => "GHOST 2.0 API is running");

app.Run();