using GHOST.Domain.Entities;
using GHOST.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("Sales", table =>
        {
            table.HasCheckConstraint("CK_Sales_Status", "[Status] IN (1, 2, 3, 4)");
            table.HasCheckConstraint("CK_Sales_Totals", "[Subtotal] >= 0 AND [Total] >= 0 AND [PaidTotal] >= 0");
            table.HasCheckConstraint("CK_Sales_TotalWhole", "[Total] = FLOOR([Total])");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.InvoiceNumber).IsRequired().HasMaxLength(30);
        builder.Property(x => x.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(x => x.DiscountValue).HasColumnType("decimal(18,2)");
        builder.Property(x => x.DiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Total).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PaidTotal).HasColumnType("decimal(18,2)");
        builder.Property(x => x.DiscountReason).HasMaxLength(Sale.MaxReasonLength);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);

        builder.HasIndex(x => x.InvoiceNumber).IsUnique().HasDatabaseName("UX_Sales_InvoiceNumber");
        builder.HasIndex(x => x.IdempotencyKey).IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL").HasDatabaseName("UX_Sales_IdempotencyKey");
        // One live invoice per session; a refund frees the session to be invoiced again.
        builder.HasIndex(x => x.SessionId).IsUnique()
            .HasFilter("[SessionId] IS NOT NULL AND [Status] IN (1, 2)").HasDatabaseName("UX_Sales_SessionId_Live");
        builder.HasIndex(x => new { x.ShiftId, x.CreatedAt }).HasDatabaseName("IX_Sales_ShiftId_CreatedAt");
        builder.HasIndex(x => x.PaidAt).HasFilter("[PaidAt] IS NOT NULL").HasDatabaseName("IX_Sales_PaidAt");

        builder.HasOne<Shift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DiscountApprovedById)
            .IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.SaleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Payments).WithOne().HasForeignKey(p => p.SaleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Refunds).WithOne().HasForeignKey(r => r.SaleId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.ToTable("SaleItems", table =>
        {
            table.HasCheckConstraint("CK_SaleItems_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_SaleItems_ItemType", "[ItemType] IN (1, 2)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DescriptionSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
        builder.Property(x => x.UnitCostSnapshot).HasColumnType("decimal(18,4)");
        builder.Property(x => x.LineTotal).HasColumnType("decimal(18,2)");

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.SaleId).HasDatabaseName("IX_SaleItems_SaleId");
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table => table.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PaidAt).HasColumnType("datetimeoffset(7)");
        builder.HasOne<Shift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReceivedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.SaleId).HasDatabaseName("IX_Payments_SaleId");
    }
}

public sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(Sale.MaxReasonLength);
        builder.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        builder.HasOne<Shift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApprovedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.SaleId).HasDatabaseName("IX_Refunds_SaleId");
    }
}
