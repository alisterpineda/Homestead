using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionShapesInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RelatedTransactionId",
                schema: "finances",
                table: "account_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_RelatedTransactionId",
                schema: "finances",
                table: "account_transactions",
                column: "RelatedTransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_account_transactions_account_transactions_RelatedTransactio~",
                schema: "finances",
                table: "account_transactions",
                column: "RelatedTransactionId",
                principalSchema: "finances",
                principalTable: "account_transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_account_transactions_account_transactions_RelatedTransactio~",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.DropIndex(
                name: "IX_account_transactions_RelatedTransactionId",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.DropColumn(
                name: "RelatedTransactionId",
                schema: "finances",
                table: "account_transactions");
        }
    }
}
