using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GHOST.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockIntakeReferenceAndMovementsAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoiceReference",
                table: "StockReceipts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "StockReceipts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
            // The append-only trigger on StockMovements is created once, by AddProductsAndStockMovements.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.DropColumn(
                name: "InvoiceReference",
                table: "StockReceipts");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "StockReceipts");
        }
    }
}
