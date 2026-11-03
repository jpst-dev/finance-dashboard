using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceDashboard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S3aLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OutTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    InTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Links_Transactions_InTransactionId",
                        column: x => x.InTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Links_Transactions_OutTransactionId",
                        column: x => x.OutTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Links_InTransactionId",
                table: "Links",
                column: "InTransactionId",
                unique: true,
                filter: "\"State\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "IX_Links_OutTransactionId",
                table: "Links",
                column: "OutTransactionId",
                unique: true,
                filter: "\"State\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "IX_Links_OutTransactionId_InTransactionId",
                table: "Links",
                columns: new[] { "OutTransactionId", "InTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Links_UserId_State",
                table: "Links",
                columns: new[] { "UserId", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Links");
        }
    }
}
