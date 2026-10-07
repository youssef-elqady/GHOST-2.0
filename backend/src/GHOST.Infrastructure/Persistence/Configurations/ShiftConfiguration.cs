using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.ToTable("Shifts");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.BusinessDayId)
            .IsRequired();

        builder.Property(s => s.StaffUserId)
            .IsRequired();

        builder.Property(s => s.OpenedAt)
            .IsRequired();

        builder.Property(s => s.ClosedAt);

        builder.Property(s => s.OpeningCash)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(s => s.ExpectedCash)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(s => s.ActualCash)
            .IsRequired(false)
            .HasColumnType("decimal(18,2)");

        builder.Property(s => s.CashDifference)
            .IsRequired(false)
            .HasColumnType("decimal(18,2)");

        builder.Property(s => s.ClosingReason)
            .HasMaxLength(500);

        builder.Property(s => s.Status)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt);

        // SQL Server rowversion for optimistic concurrency.
        builder.Property<byte[]>("RowVersion")
            .IsRowVersion();

        builder.HasIndex(s => s.BusinessDayId);
        builder.HasIndex(s => s.StaffUserId);
        builder.HasIndex(s => s.Status);

        // Single physical cash register: at most one open shift globally.
        builder.HasIndex(s => s.Status)
            .HasFilter("[Status] = 1")
            .IsUnique();
    }
}
