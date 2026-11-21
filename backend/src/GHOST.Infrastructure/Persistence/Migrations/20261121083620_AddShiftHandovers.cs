using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GHOST.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftHandovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShiftHandovers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HandedCash = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReceivedCash = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Difference = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AcknowledgedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftHandovers", x => x.Id);
                    table.CheckConstraint("CK_ShiftHandovers_Amounts", "[HandedCash] >= 0 AND [ReceivedCash] >= 0");
                    table.CheckConstraint("CK_ShiftHandovers_DifferentShifts", "[FromShiftId] <> [ToShiftId]");
                    table.ForeignKey(
                        name: "FK_ShiftHandovers_Shifts_FromShiftId",
                        column: x => x.FromShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftHandovers_Shifts_ToShiftId",
                        column: x => x.ToShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftHandovers_Users_AcknowledgedById",
                        column: x => x.AcknowledgedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftHandovers_AcknowledgedById",
                table: "ShiftHandovers",
                column: "AcknowledgedById");

            migrationBuilder.CreateIndex(
                name: "UX_ShiftHandovers_FromShiftId",
                table: "ShiftHandovers",
                column: "FromShiftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ShiftHandovers_ToShiftId",
                table: "ShiftHandovers",
                column: "ToShiftId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShiftHandovers");
        }
    }
}
