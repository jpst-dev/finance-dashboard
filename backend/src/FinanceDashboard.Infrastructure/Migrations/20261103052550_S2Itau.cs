using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceDashboard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S2Itau : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardKind",
                table: "Transactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardSuffix",
                table: "Transactions",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InstallmentNumber",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InstallmentTotal",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DeclaredTotal",
                table: "ImportBatches",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "ImportBatches",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LedgerBalance",
                table: "ImportBatches",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LedgerBalanceDate",
                table: "ImportBatches",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardKind",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CardSuffix",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "InstallmentNumber",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "InstallmentTotal",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeclaredTotal",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "LedgerBalance",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "LedgerBalanceDate",
                table: "ImportBatches");
        }
    }
}
