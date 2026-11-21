using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GHOST.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowShiftCloseBackupType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BackupRecords_Type",
                table: "BackupRecords");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BackupRecords_Type",
                table: "BackupRecords",
                sql: "[Type] IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BackupRecords_Type",
                table: "BackupRecords");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BackupRecords_Type",
                table: "BackupRecords",
                sql: "[Type] IN (1, 2)");
        }
    }
}
