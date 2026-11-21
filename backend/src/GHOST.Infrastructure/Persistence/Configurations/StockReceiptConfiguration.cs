using GHOST.Domain.Entities;
using GHOST.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class StockReceiptConfiguration : IEntityTypeConfiguration<StockReceipt>
{
    public void Configure(EntityTypeBuilder<StockReceipt> builder)
    {
        builder.ToTable("StockReceipts", table =>
        {
            table.HasCheckConstraint("CK_StockReceipts_Status", "[Status] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_StockReceipts_TotalCost", "[TotalCost] >= 0");
        });

        // Guards approve vs reject: only one decision can win.
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SupplierName).HasMaxLength(150);
        builder.Property(x => x.InvoiceReference).HasMaxLength(StockReceipt.MaxReferenceLength);
        builder.Property(x => x.Notes).HasMaxLength(StockReceipt.MaxNotesLength);
        builder.Property(x => x.TotalCost).HasColumnType("decimal(18,2)");
        builder.Property(x => x.RejectionReason).HasMaxLength(500);

        builder.HasOne<Shift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DecidedById)
            .IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("IX_StockReceipts_Status_CreatedAt");
    }
}

public sealed class StockReceiptItemConfiguration : IEntityTypeConfiguration<StockReceiptItem>
{
    public void Configure(EntityTypeBuilder<StockReceiptItem> builder)
    {
        builder.ToTable("StockReceiptItems", table =>
        {
            table.HasCheckConstraint("CK_StockReceiptItems_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_StockReceiptItems_UnitCost", "[UnitCost] >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.UnitCost).HasColumnType("decimal(18,4)");
        builder.Ignore(x => x.LineTotal);

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ReceiptId).HasDatabaseName("IX_StockReceiptItems_ReceiptId");
    }
}
