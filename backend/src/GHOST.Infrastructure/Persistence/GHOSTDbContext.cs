using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence;

public sealed class GHOSTDbContext : DbContext
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceRate> DeviceRates => Set<DeviceRate>();
    public DbSet<BusinessDay> BusinessDays => Set<BusinessDay>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionSegment> SessionSegments => Set<SessionSegment>();
    public DbSet<SessionPause> SessionPauses => Set<SessionPause>();

    public GHOSTDbContext(DbContextOptions<GHOSTDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GHOSTDbContext).Assembly);
    }
}