using GHOST.Domain.Entities;
using GHOST.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs", table =>
        {
            table.HasCheckConstraint(
                "CK_AuditLogs_Actor",
                "(([ActorType] = N'System' AND [UserId] IS NULL) OR ([ActorType] = N'User' AND [UserId] IS NOT NULL))");

            table.HasCheckConstraint(
                "CK_AuditLogs_ActorType",
                "[ActorType] IN (N'System', N'User')");

            table.HasCheckConstraint(
                "CK_AuditLogs_OldValues_Json",
                "[OldValues] IS NULL OR (ISJSON([OldValues]) = 1 AND DATALENGTH([OldValues]) <= 16384)");

            table.HasCheckConstraint(
                "CK_AuditLogs_NewValues_Json",
                "[NewValues] IS NULL OR (ISJSON([NewValues]) = 1 AND DATALENGTH([NewValues]) <= 16384)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActorType)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(x => x.Action)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.EntityType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.EntityId)
            .HasMaxLength(128);

        builder.Property(x => x.OldValues)
            .HasColumnType("nvarchar(max)");

        builder.Property(x => x.NewValues)
            .HasColumnType("nvarchar(max)");

        builder.Property(x => x.At)
            .IsRequired()
            .HasColumnType("datetimeoffset(7)");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.EntityType, x.EntityId, x.At })
            .HasDatabaseName("IX_AuditLogs_EntityType_EntityId_At");

        builder.HasIndex(x => new { x.UserId, x.At })
            .HasDatabaseName("IX_AuditLogs_UserId_At");
    }
}
