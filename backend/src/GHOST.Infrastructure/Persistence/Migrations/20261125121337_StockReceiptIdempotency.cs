using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GHOST.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockReceiptIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockReceipts_CreatedById",
                table: "StockReceipts");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "StockReceipts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "StockReceipts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_StockReceipts_CreatedById_IdempotencyKey",
                table: "StockReceipts",
                columns: new[] { "CreatedById", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_StockReceipts_CreatedById_IdempotencyKey",
                table: "StockReceipts");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "StockReceipts");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "StockReceipts");

            migrationBuilder.CreateIndex(
                name: "IX_StockReceipts_CreatedById",
                table: "StockReceipts",
                column: "CreatedById");
        }
    }
}
