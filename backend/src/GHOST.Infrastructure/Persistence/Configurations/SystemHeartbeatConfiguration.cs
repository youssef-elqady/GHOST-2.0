using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class SystemHeartbeatConfiguration : IEntityTypeConfiguration<SystemHeartbeat>
{
    public void Configure(EntityTypeBuilder<SystemHeartbeat> builder)
    {
        builder.ToTable("SystemHeartbeats", table =>
            table.HasCheckConstraint("CK_SystemHeartbeats_Singleton", "[Id] = 1"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.BeatAt).IsRequired();
    }
}
