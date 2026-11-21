using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GHOST.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSettingsBackupsAndReportIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PrunedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRecords", x => x.Id);
                    table.CheckConstraint("CK_BackupRecords_Status", "[Status] IN (1, 2)");
                    table.CheckConstraint("CK_BackupRecords_Type", "[Type] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_BackupRecords_Users_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SystemSettings_Users_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_ClosedAt",
                table: "Shifts",
                column: "ClosedAt",
                filter: "[ClosedAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_CancelledAt",
                table: "Sessions",
                column: "CancelledAt",
                filter: "[CancelledAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_PaidAt",
                table: "Sales",
                column: "PaidAt",
                filter: "[PaidAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_Type_CreatedAt",
                table: "CashTransactions",
                columns: new[] { "Type", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_Live",
                table: "BackupRecords",
                column: "CreatedAt",
                descending: new bool[0],
                filter: "[Status] = 1 AND [PrunedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_RequestedById",
                table: "BackupRecords",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_UpdatedById",
                table: "SystemSettings",
                column: "UpdatedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupRecords");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_ClosedAt",
                table: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_CancelledAt",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sales_PaidAt",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_Type_CreatedAt",
                table: "CashTransactions");
        }
    }
}
