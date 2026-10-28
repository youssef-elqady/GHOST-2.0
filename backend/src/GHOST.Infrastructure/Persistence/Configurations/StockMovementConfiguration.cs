using GHOST.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GHOST.Infrastructure.Persistence.Configurations;

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", table =>
        {
            table.HasCheckConstraint("CK_StockMovements_QuantityDelta_NonZero", "[QuantityDelta] <> 0");
            table.HasCheckConstraint("CK_StockMovements_UnitCost_NonNegative", "[UnitCost] >= 0");
            table.HasCheckConstraint("CK_StockMovements_BalanceAfter_NonNegative", "[BalanceAfter] >= 0");
        });

        builder.HasKey(movement => movement.Id);
        builder.Property(movement => movement.ProductId).IsRequired();
        builder.Property(movement => movement.Type).IsRequired();
        builder.Property(movement => movement.QuantityDelta)
            .HasColumnType("decimal(18,3)")
            .IsRequired();
        builder.Property(movement => movement.UnitCost)
            .HasColumnType("decimal(18,4)")
            .IsRequired();
        builder.Property(movement => movement.BalanceAfter)
            .HasColumnType("decimal(18,3)")
            .IsRequired();
        builder.Property(movement => movement.Reference).HasMaxLength(100);
        builder.Property(movement => movement.Notes).HasMaxLength(500);
        builder.Property(movement => movement.CreatedAt).IsRequired();
        builder.Property(movement => movement.UpdatedAt);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(movement => movement.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(movement => new { movement.ProductId, movement.CreatedAt });
        builder.HasIndex(movement => movement.Type);
        builder.HasIndex(movement => movement.Reference);
    }
}
