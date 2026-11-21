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

            migrationBuilder.Sql("""
                CREATE TRIGGER [dbo].[TR_StockMovements_AppendOnly]
                ON [dbo].[StockMovements]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51002, 'StockMovements is append-only.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[dbo].[TR_StockMovements_AppendOnly]', N'TR') IS NOT NULL
                    DROP TRIGGER [dbo].[TR_StockMovements_AppendOnly];
                """);

            migrationBuilder.DropColumn(
                name: "InvoiceReference",
                table: "StockReceipts");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "StockReceipts");
        }
    }
}
