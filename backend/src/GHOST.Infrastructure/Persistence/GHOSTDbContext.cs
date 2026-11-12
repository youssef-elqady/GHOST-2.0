using GHOST.Domain.Entities;
using GHOST.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GHOST.Infrastructure.Persistence;

public sealed class GHOSTDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
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

        // Keep Identity's standard schema and indexes, but use the business
        // table names documented in the GHOST SRS.
        modelBuilder.Entity<ApplicationUser>().ToTable("Users");
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("Roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.FullName)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(user => user.IsActive)
                .IsRequired();

            entity.Property(user => user.MustChangePassword)
                .IsRequired();

            entity.Property(user => user.CreatedAt)
                .IsRequired();

            entity.Property(user => user.CreatedByAdminId);
        });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GHOSTDbContext).Assembly);
    }
}