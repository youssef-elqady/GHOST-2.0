using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
        {
            table.HasCheckConstraint("CK_Products_SellingPrice_Positive", "[SellingPrice] > 0");
            table.HasCheckConstraint("CK_Products_AverageUnitCost_NonNegative", "[AverageUnitCost] >= 0");
            table.HasCheckConstraint("CK_Products_StockQuantity_NonNegative", "[StockQuantity] >= 0");
            table.HasCheckConstraint("CK_Products_MinimumStock_NonNegative", "[MinimumStock] >= 0");
        });

        builder.HasKey(product => product.Id);

        builder.Property(product => product.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(product => product.Sku)
            .HasMaxLength(64);

        builder.Property(product => product.Category)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(product => product.Unit)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(product => product.SellingPrice)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(product => product.AverageUnitCost)
            .HasColumnType("decimal(18,4)")
            .IsRequired();

        builder.Property(product => product.StockQuantity)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(product => product.MinimumStock)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(product => product.IsActive).IsRequired();
        builder.Property(product => product.CreatedAt).IsRequired();
        builder.Property(product => product.UpdatedAt);

        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.HasIndex(product => product.Name);
        builder.HasIndex(product => product.Category);
        builder.HasIndex(product => product.IsActive);
        builder.HasIndex(product => product.Sku)
            .IsUnique()
            .HasFilter("[Sku] IS NOT NULL");
    }
}
