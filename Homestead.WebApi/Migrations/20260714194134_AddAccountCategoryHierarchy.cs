using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountCategoryHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentAccountId",
                schema: "finances",
                table: "accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_ParentAccountId_AccountType_Name",
                schema: "finances",
                table: "accounts",
                columns: new[] { "ParentAccountId", "AccountType", "Name" },
                unique: true,
                filter: "\"AccountType\" IN ('Income', 'Expense')")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddForeignKey(
                name: "FK_accounts_accounts_ParentAccountId",
                schema: "finances",
                table: "accounts",
                column: "ParentAccountId",
                principalSchema: "finances",
                principalTable: "accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_accounts_accounts_ParentAccountId",
                schema: "finances",
                table: "accounts");

            migrationBuilder.DropIndex(
                name: "IX_accounts_ParentAccountId_AccountType_Name",
                schema: "finances",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "ParentAccountId",
                schema: "finances",
                table: "accounts");
        }
    }
}
