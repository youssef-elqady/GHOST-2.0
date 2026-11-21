using GHOST.Domain.Entities;
using GHOST.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
        {
            table.HasCheckConstraint("CK_Products_StockQuantity", "[StockQuantity] >= 0");
            table.HasCheckConstraint("CK_Products_SalePrice", "[SalePrice] >= 0");
            table.HasCheckConstraint("CK_Products_Status", "[Status] IN (1, 2, 3)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
        builder.Property(x => x.Barcode).HasMaxLength(64);
        builder.Property(x => x.Sku).HasMaxLength(64);
        builder.Property(x => x.Category).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Unit).IsRequired().HasMaxLength(30);

        builder.Property(x => x.SalePrice).HasColumnType("decimal(18,2)");
        builder.Property(x => x.AvgCost).HasColumnType("decimal(18,4)");
        builder.Property(x => x.StockQuantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.MinStock).HasColumnType("decimal(18,3)");
        builder.Property(x => x.WarningStock).HasColumnType("decimal(18,3)");

        // Concurrency token: two simultaneous sales cannot both take the last unit (FR-37).
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.Ignore(x => x.IsSellable);
        builder.Ignore(x => x.StockLevel);

        builder.HasIndex(x => x.Barcode)
            .IsUnique()
            .HasFilter("[Barcode] IS NOT NULL")
            .HasDatabaseName("UX_Products_Barcode");

        builder.HasIndex(x => x.Sku)
            .IsUnique()
            .HasFilter("[Sku] IS NOT NULL")
            .HasDatabaseName("UX_Products_Sku");

        builder.HasIndex(x => x.Name).HasDatabaseName("IX_Products_Name");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Movements)
            .WithOne()
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.PriceHistory)
            .WithOne()
            .HasForeignKey(h => h.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(x => x.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.PriceHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", table =>
        {
            table.HasCheckConstraint("CK_StockMovements_QtyDelta", "[QtyDelta] <> 0");
            table.HasCheckConstraint("CK_StockMovements_BalanceAfter", "[BalanceAfter] >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Type).IsRequired();
        builder.Property(x => x.QtyDelta).HasColumnType("decimal(18,3)");
        builder.Property(x => x.UnitCost).HasColumnType("decimal(18,4)");
        builder.Property(x => x.BalanceAfter).HasColumnType("decimal(18,3)");
        builder.Property(x => x.SourceType).HasMaxLength(50);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(StockMovement.MaxReasonLength);
        builder.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");

        builder.HasOne<Shift>()
            .WithMany()
            .HasForeignKey(x => x.ShiftId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ProductId, x.CreatedAt })
            .HasDatabaseName("IX_StockMovements_ProductId_CreatedAt");
    }
}

public sealed class ProductPriceHistoryConfiguration : IEntityTypeConfiguration<ProductPriceHistory>
{
    public void Configure(EntityTypeBuilder<ProductPriceHistory> builder)
    {
        builder.ToTable("ProductPriceHistory");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OldValue).HasColumnType("decimal(18,4)");
        builder.Property(x => x.NewValue).HasColumnType("decimal(18,4)");
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.ChangedAt).HasColumnType("datetimeoffset(7)");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.ChangedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ProductId, x.ChangedAt })
            .HasDatabaseName("IX_ProductPriceHistory_ProductId_ChangedAt");
    }
}
