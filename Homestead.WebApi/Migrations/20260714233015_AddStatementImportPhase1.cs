using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddStatementImportPhase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Result",
                schema: "processing",
                table: "jobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountNumber",
                schema: "finances",
                table: "accounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceDocumentVersionId",
                schema: "finances",
                table: "account_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_SourceDocumentVersionId",
                schema: "finances",
                table: "account_transactions",
                column: "SourceDocumentVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_account_transactions_SourceDocumentVersionId",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.DropColumn(
                name: "Result",
                schema: "processing",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "AccountNumber",
                schema: "finances",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "SourceDocumentVersionId",
                schema: "finances",
                table: "account_transactions");
        }
    }
}
