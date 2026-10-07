using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class SessionPauseConfiguration : IEntityTypeConfiguration<SessionPause>
{
    public void Configure(EntityTypeBuilder<SessionPause> builder)
    {
        builder.ToTable("SessionPauses");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.SessionSegmentId)
            .IsRequired();

        builder.Property(p => p.StartedAt)
            .IsRequired();

        builder.Property(p => p.EndedAt);

        builder.Property(p => p.Reason)
            .HasMaxLength(500);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt);

        // Supports loading all pauses for a segment.
        builder.HasIndex(p => p.SessionSegmentId);

        // A segment can have multiple historical pauses, but only one active pause.
        builder.HasIndex(p => p.SessionSegmentId)
            .HasFilter("[EndedAt] IS NULL")
            .IsUnique();
    }
}
