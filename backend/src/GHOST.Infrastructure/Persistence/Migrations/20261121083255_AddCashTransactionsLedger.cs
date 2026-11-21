using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GHOST.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashTransactionsLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BeneficiaryUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashTransactions", x => x.Id);
                    table.CheckConstraint("CK_CashTransactions_Amount", "[Amount] > 0 OR ([Amount] = 0 AND [Type] = 1)");
                    table.CheckConstraint("CK_CashTransactions_Direction", "[Direction] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_CashTransactions_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashTransactions_Users_BeneficiaryUserId",
                        column: x => x.BeneficiaryUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashTransactions_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_BeneficiaryUserId",
                table: "CashTransactions",
                column: "BeneficiaryUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_CreatedById",
                table: "CashTransactions",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_ShiftId_CreatedAt",
                table: "CashTransactions",
                columns: new[] { "ShiftId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_CashTransactions_ShiftId_IdempotencyKey",
                table: "CashTransactions",
                columns: new[] { "ShiftId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            // Existing shifts predate the ledger: give each one its OpeningBalance entry so
            // the ledger reconciles with Shifts.OpeningCash. Later movements cannot exist yet.
            migrationBuilder.Sql("""
                INSERT INTO [dbo].[CashTransactions]
                    ([Id], [ShiftId], [Type], [Direction], [Amount], [CreatedById], [CreatedAt])
                SELECT NEWID(), s.[Id], 1, 1, s.[OpeningCash], s.[StaffUserId], s.[OpenedAt]
                FROM [dbo].[Shifts] s
                WHERE NOT EXISTS (
                    SELECT 1 FROM [dbo].[CashTransactions] c
                    WHERE c.[ShiftId] = s.[Id] AND c.[Type] = 1);
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [dbo].[TR_CashTransactions_AppendOnly]
                ON [dbo].[CashTransactions]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51001, 'CashTransactions is append-only.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[dbo].[TR_CashTransactions_AppendOnly]', N'TR') IS NOT NULL
                    DROP TRIGGER [dbo].[TR_CashTransactions_AppendOnly];
                """);

            migrationBuilder.DropTable(
                name: "CashTransactions");
        }
    }
}
