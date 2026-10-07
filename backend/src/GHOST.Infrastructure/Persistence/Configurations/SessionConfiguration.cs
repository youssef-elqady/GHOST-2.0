using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("Sessions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.StartShiftId)
            .IsRequired();

        builder.Property(s => s.EndShiftId);

        builder.Property(s => s.Status)
            .IsRequired();

        builder.Property(s => s.CancellationReason)
            .HasMaxLength(500);

        builder.Property(s => s.CancelledAt);

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt);

        // SQL Server rowversion for optimistic concurrency.
        builder.Property<byte[]>("RowVersion")
            .IsRowVersion();

        builder.HasMany(s => s.Segments)
            .WithOne()
            .HasForeignKey(seg => seg.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Metadata.FindNavigation(nameof(Session.Segments))?
            .SetField("_segments");
        builder.Metadata.FindNavigation(nameof(Session.Segments))?
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Shift>()
            .WithMany()
            .HasForeignKey(s => s.StartShiftId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Shift>()
            .WithMany()
            .HasForeignKey(s => s.EndShiftId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.StartShiftId);
        builder.HasIndex(s => s.EndShiftId);
        builder.HasIndex(s => s.Status);
    }
}
