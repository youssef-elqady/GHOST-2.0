using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class DeviceRateConfiguration : IEntityTypeConfiguration<DeviceRate>
{
    public void Configure(EntityTypeBuilder<DeviceRate> builder)
    {
        builder.ToTable("DeviceRates");

        builder.HasKey(dr => dr.Id);

        builder.Property(dr => dr.DeviceId)
            .IsRequired();

        builder.Property(dr => dr.PlayType)
            .IsRequired();

        builder.Property(dr => dr.HourlyRate)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(dr => dr.CreatedAt)
            .IsRequired();

        builder.Property(dr => dr.UpdatedAt);

        builder.HasIndex(dr => new { dr.DeviceId, dr.PlayType })
            .IsUnique();
    }
}
