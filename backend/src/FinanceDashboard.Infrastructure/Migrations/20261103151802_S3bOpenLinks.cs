using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceDashboard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S3bOpenLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "OutTransactionId",
                table: "Links",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "InTransactionId",
                table: "Links",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "OwnAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Fragment = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnAliases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Links_InTransactionId_LiveOpen",
                table: "Links",
                column: "InTransactionId",
                unique: true,
                filter: "\"OutTransactionId\" IS NULL AND \"State\" <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_Links_OutTransactionId_LiveOpen",
                table: "Links",
                column: "OutTransactionId",
                unique: true,
                filter: "\"InTransactionId\" IS NULL AND \"State\" <> 'Rejected'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Links_AtLeastOneSide",
                table: "Links",
                sql: "\"OutTransactionId\" IS NOT NULL OR \"InTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OwnAliases_UserId_Fragment",
                table: "OwnAliases",
                columns: new[] { "UserId", "Fragment" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Open links (one side null) cannot exist under the S3(a) NOT NULL columns; they are discarded.
            migrationBuilder.Sql("DELETE FROM \"Links\" WHERE \"OutTransactionId\" IS NULL OR \"InTransactionId\" IS NULL;");

            migrationBuilder.DropTable(
                name: "OwnAliases");

            migrationBuilder.DropIndex(
                name: "IX_Links_InTransactionId_LiveOpen",
                table: "Links");

            migrationBuilder.DropIndex(
                name: "IX_Links_OutTransactionId_LiveOpen",
                table: "Links");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Links_AtLeastOneSide",
                table: "Links");

            migrationBuilder.AlterColumn<Guid>(
                name: "OutTransactionId",
                table: "Links",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "InTransactionId",
                table: "Links",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
