using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class SessionSegmentConfiguration : IEntityTypeConfiguration<SessionSegment>
{
    public void Configure(EntityTypeBuilder<SessionSegment> builder)
    {
        builder.ToTable("SessionSegments");

        builder.HasKey(seg => seg.Id);

        builder.Property(seg => seg.SessionId)
            .IsRequired();

        builder.Property(seg => seg.DeviceId)
            .IsRequired();

        builder.Property(seg => seg.PlayType)
            .IsRequired();

        builder.Property(seg => seg.AppliedHourlyRate)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(seg => seg.StartedAt)
            .IsRequired();

        builder.Property(seg => seg.EndedAt);

        builder.Property(seg => seg.CreatedAt)
            .IsRequired();

        builder.Property(seg => seg.UpdatedAt);

        builder.HasMany(seg => seg.Pauses)
            .WithOne()
            .HasForeignKey(p => p.SessionSegmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Metadata.FindNavigation(nameof(SessionSegment.Pauses))?
            .SetField("_pauses");
        builder.Metadata.FindNavigation(nameof(SessionSegment.Pauses))?
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(seg => seg.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(seg => seg.SessionId);
        builder.HasIndex(seg => seg.DeviceId);
        builder.HasIndex(seg => seg.EndedAt);

        // Enforce at most one active segment per Session (EndedAt IS NULL)
        builder.HasIndex(seg => seg.SessionId)
            .HasFilter("[EndedAt] IS NULL")
            .IsUnique();

        // Enforce at most one active segment per Device (EndedAt IS NULL)
        builder.HasIndex(seg => seg.DeviceId)
            .HasFilter("[EndedAt] IS NULL")
            .IsUnique();
    }
}