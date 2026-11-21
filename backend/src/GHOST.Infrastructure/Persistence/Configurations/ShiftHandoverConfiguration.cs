using GHOST.Domain.Entities;
using GHOST.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class ShiftHandoverConfiguration : IEntityTypeConfiguration<ShiftHandover>
{
    public void Configure(EntityTypeBuilder<ShiftHandover> builder)
    {
        builder.ToTable("ShiftHandovers", table =>
        {
            table.HasCheckConstraint("CK_ShiftHandovers_DifferentShifts", "[FromShiftId] <> [ToShiftId]");
            table.HasCheckConstraint("CK_ShiftHandovers_Amounts", "[HandedCash] >= 0 AND [ReceivedCash] >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.HandedCash).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(x => x.ReceivedCash).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(x => x.Difference).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(x => x.Note).HasMaxLength(ShiftHandover.MaxNoteLength);
        builder.Property(x => x.AcknowledgedAt).IsRequired().HasColumnType("datetimeoffset(7)");
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasOne<Shift>().WithMany().HasForeignKey(x => x.FromShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Shift>().WithMany().HasForeignKey(x => x.ToShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AcknowledgedById)
            .OnDelete(DeleteBehavior.Restrict);

        // A shift hands over once and receives once (ERD 6.7: 1-1 from, 1-1 to).
        builder.HasIndex(x => x.FromShiftId).IsUnique().HasDatabaseName("UX_ShiftHandovers_FromShiftId");
        builder.HasIndex(x => x.ToShiftId).IsUnique().HasDatabaseName("UX_ShiftHandovers_ToShiftId");
    }
}
