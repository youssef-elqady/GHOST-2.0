using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class BusinessDayConfiguration : IEntityTypeConfiguration<BusinessDay>
{
    public void Configure(EntityTypeBuilder<BusinessDay> builder)
    {
        builder.ToTable("BusinessDays");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.BusinessDate)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(b => b.IsClosed)
            .IsRequired();

        builder.Property(b => b.OpenedAt)
            .IsRequired();

        builder.Property(b => b.ClosedAt);

        builder.Property(b => b.CreatedAt)
            .IsRequired();

        builder.Property(b => b.UpdatedAt);

        builder.HasMany(b => b.Shifts)
            .WithOne()
            .HasForeignKey(s => s.BusinessDayId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Metadata.FindNavigation(nameof(BusinessDay.Shifts))?
            .SetField("_shifts");
        builder.Metadata.FindNavigation(nameof(BusinessDay.Shifts))?
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(b => b.BusinessDate)
            .IsUnique();
    }
}