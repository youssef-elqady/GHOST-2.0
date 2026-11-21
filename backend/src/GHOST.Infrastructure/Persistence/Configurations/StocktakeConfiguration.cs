using GHOST.Domain.Entities;
using GHOST.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class StocktakeConfiguration : IEntityTypeConfiguration<Stocktake>
{
    public void Configure(EntityTypeBuilder<Stocktake> builder)
    {
        builder.ToTable("Stocktakes", table =>
            table.HasCheckConstraint("CK_Stocktakes_Status", "[Status] IN (1, 2, 3, 4)"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DecisionNote).HasMaxLength(500);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StartedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DecidedById)
            .IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.StocktakeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        // At most one open and one submitted stocktake at a time (the handler allows only one active).
        builder.HasIndex(x => x.Status).IsUnique()
            .HasFilter("[Status] IN (1, 2)").HasDatabaseName("UX_Stocktakes_OnePerActiveState");
        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}

public sealed class StocktakeItemConfiguration : IEntityTypeConfiguration<StocktakeItem>
{
    public void Configure(EntityTypeBuilder<StocktakeItem> builder)
    {
        builder.ToTable("StocktakeItems");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ProductName).IsRequired().HasMaxLength(150);
        builder.Property(x => x.SystemQuantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.CountedQuantity).HasColumnType("decimal(18,3)");
        builder.Ignore(x => x.Difference);

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.StocktakeId, x.ProductId }).IsUnique().HasDatabaseName("UX_StocktakeItems_Stocktake_Product");
    }
}
