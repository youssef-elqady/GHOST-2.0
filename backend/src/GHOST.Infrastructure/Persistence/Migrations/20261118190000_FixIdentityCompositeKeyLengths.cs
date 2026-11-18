using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GHOST.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixIdentityCompositeKeyLengths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Refuse to narrow any existing value. DATALENGTH is measured in bytes;
            // nvarchar(128) supports at most 256 bytes.
            migrationBuilder.Sql("""
                IF EXISTS
                (
                    SELECT 1
                    FROM [dbo].[UserLogins]
                    WHERE DATALENGTH([LoginProvider]) > 256
                       OR DATALENGTH([ProviderKey]) > 256
                )
                    THROW 51001, 'Identity UserLogins contains a key value longer than 128 characters; no schema changes were applied.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [dbo].[UserTokens]
                    WHERE DATALENGTH([LoginProvider]) > 256
                       OR DATALENGTH([Name]) > 256
                )
                    THROW 51002, 'Identity UserTokens contains a key value longer than 128 characters; no schema changes were applied.', 1;
                """);

            migrationBuilder.DropPrimaryKey(name: "PK_UserLogins", table: "UserLogins");
            migrationBuilder.DropPrimaryKey(name: "PK_UserTokens", table: "UserTokens");

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider", table: "UserLogins",
                type: "nvarchar(128)", maxLength: 128, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderKey", table: "UserLogins",
                type: "nvarchar(128)", maxLength: 128, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider", table: "UserTokens",
                type: "nvarchar(128)", maxLength: 128, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Name", table: "UserTokens",
                type: "nvarchar(128)", maxLength: 128, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(450)");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserLogins", table: "UserLogins",
                columns: new[] { "LoginProvider", "ProviderKey" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserTokens", table: "UserTokens",
                columns: new[] { "UserId", "LoginProvider", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_UserLogins", table: "UserLogins");
            migrationBuilder.DropPrimaryKey(name: "PK_UserTokens", table: "UserTokens");

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider", table: "UserLogins",
                type: "nvarchar(450)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderKey", table: "UserLogins",
                type: "nvarchar(450)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider", table: "UserTokens",
                type: "nvarchar(450)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "Name", table: "UserTokens",
                type: "nvarchar(450)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserLogins", table: "UserLogins",
                columns: new[] { "LoginProvider", "ProviderKey" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserTokens", table: "UserTokens",
                columns: new[] { "UserId", "LoginProvider", "Name" });
        }
    }
}
