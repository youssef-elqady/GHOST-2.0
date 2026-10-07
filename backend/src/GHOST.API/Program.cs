using GHOST.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<GHOSTDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.MigrationsAssembly(
            typeof(GHOSTDbContext).Assembly.FullName)));

var app = builder.Build();

app.MapControllers();

app.MapGet("/", () => "GHOST 2.0 API is running");

app.Run();