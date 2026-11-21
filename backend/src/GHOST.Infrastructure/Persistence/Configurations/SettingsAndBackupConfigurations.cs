using GHOST.Domain.Entities;
using GHOST.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSettings");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(SystemSetting.MaxKeyLength);
        builder.Property(x => x.Value).IsRequired().HasMaxLength(SystemSetting.MaxValueLength);
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UpdatedById).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BackupRecordConfiguration : IEntityTypeConfiguration<BackupRecord>
{
    public void Configure(EntityTypeBuilder<BackupRecord> builder)
    {
        builder.ToTable("BackupRecords", table =>
        {
            table.HasCheckConstraint("CK_BackupRecords_Type", "[Type] IN (1, 2)");
            table.HasCheckConstraint("CK_BackupRecords_Status", "[Status] IN (1, 2)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FilePath).HasMaxLength(BackupRecord.MaxPathLength);
        builder.Property(x => x.Error).HasMaxLength(BackupRecord.MaxErrorLength);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequestedById)
            .IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CreatedAt).IsDescending().HasDatabaseName("IX_BackupRecords_CreatedAt");
        builder.HasIndex(x => x.CreatedAt).HasFilter("[Status] = 1 AND [PrunedAt] IS NULL")
            .HasDatabaseName("IX_BackupRecords_Live");
    }
}
