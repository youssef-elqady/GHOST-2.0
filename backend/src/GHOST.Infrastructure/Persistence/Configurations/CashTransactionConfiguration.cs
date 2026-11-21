using GHOST.Domain.Entities;
using GHOST.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class CashTransactionConfiguration : IEntityTypeConfiguration<CashTransaction>
{
    public void Configure(EntityTypeBuilder<CashTransaction> builder)
    {
        builder.ToTable("CashTransactions", table =>
        {
            table.HasCheckConstraint(
                "CK_CashTransactions_Amount",
                "[Amount] > 0 OR ([Amount] = 0 AND [Type] = 1)");

            table.HasCheckConstraint(
                "CK_CashTransactions_Direction",
                "[Direction] IN (1, 2)");
        });

        builder.HasKey(x => x.Id);

        // Ids are assigned by the domain; EF must treat new ledger rows as inserts.
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Type).IsRequired();
        builder.Property(x => x.Direction).IsRequired();

        builder.Property(x => x.Amount)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(x => x.Category).HasMaxLength(CashTransaction.MaxCategoryLength);
        builder.Property(x => x.SourceType).HasMaxLength(50);
        builder.Property(x => x.Reason).HasMaxLength(CashTransaction.MaxReasonLength);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(CashTransaction.MaxIdempotencyKeyLength);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnType("datetimeoffset(7)");

        builder.Ignore(x => x.SignedAmount);

        builder.HasOne<Shift>()
            .WithMany(shift => shift.CashTransactions)
            .HasForeignKey(x => x.ShiftId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.BeneficiaryUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ShiftId, x.CreatedAt })
            .HasDatabaseName("IX_CashTransactions_ShiftId_CreatedAt");

        // Report and alert queries filter by type and time range across all shifts.
        builder.HasIndex(x => new { x.Type, x.CreatedAt })
            .HasDatabaseName("IX_CashTransactions_Type_CreatedAt");

        // One entry per idempotency key within a shift; null keys are unconstrained.
        builder.HasIndex(x => new { x.ShiftId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL")
            .HasDatabaseName("UX_CashTransactions_ShiftId_IdempotencyKey");
    }
}
