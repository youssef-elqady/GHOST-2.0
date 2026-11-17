using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GHOST.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.CheckConstraint("CK_AuditLogs_Actor", "(([ActorType] = N'System' AND [UserId] IS NULL) OR ([ActorType] = N'User' AND [UserId] IS NOT NULL))");
                    table.CheckConstraint("CK_AuditLogs_ActorType", "[ActorType] IN (N'System', N'User')");
                    table.CheckConstraint("CK_AuditLogs_NewValues_Json", "[NewValues] IS NULL OR (ISJSON([NewValues]) = 1 AND DATALENGTH([NewValues]) <= 16384)");
                    table.CheckConstraint("CK_AuditLogs_OldValues_Json", "[OldValues] IS NULL OR (ISJSON([OldValues]) = 1 AND DATALENGTH([OldValues]) <= 16384)");
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId_At",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId_At",
                table: "AuditLogs",
                columns: new[] { "UserId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");
        }
    }
}
