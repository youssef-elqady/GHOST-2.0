using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GHOST.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PowerOutageReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NeedsReview",
                table: "Sessions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OutageEndedAt",
                table: "Sessions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OutageStartedAt",
                table: "Sessions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "Sessions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedById",
                table: "Sessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SystemHeartbeats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    BeatAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemHeartbeats", x => x.Id);
                    table.CheckConstraint("CK_SystemHeartbeats_Singleton", "[Id] = 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_NeedsReview",
                table: "Sessions",
                column: "NeedsReview",
                filter: "[NeedsReview] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemHeartbeats");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_NeedsReview",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "NeedsReview",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "OutageEndedAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "OutageStartedAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ReviewedById",
                table: "Sessions");
        }
    }
}
