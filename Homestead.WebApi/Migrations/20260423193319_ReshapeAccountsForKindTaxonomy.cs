using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeAccountsForKindTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_accounts_accounts_ParentId",
                schema: "finances",
                table: "accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_accounts_commodities_DefaultCommodityId",
                schema: "finances",
                table: "accounts");

            migrationBuilder.DropIndex(
                name: "IX_accounts_AccountType",
                schema: "finances",
                table: "accounts");

            migrationBuilder.DropIndex(
                name: "IX_accounts_ParentId",
                schema: "finances",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "IsMultiPosition",
                schema: "finances",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "ParentId",
                schema: "finances",
                table: "accounts");

            migrationBuilder.RenameColumn(
                name: "DefaultCommodityId",
                schema: "finances",
                table: "accounts",
                newName: "ReportingCurrencyId");

            migrationBuilder.RenameIndex(
                name: "IX_accounts_DefaultCommodityId",
                schema: "finances",
                table: "accounts",
                newName: "IX_accounts_ReportingCurrencyId");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "finances",
                table: "accounts",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_Kind",
                schema: "finances",
                table: "accounts",
                column: "Kind",
                filter: "\"IsArchived\" = FALSE");

            migrationBuilder.AddForeignKey(
                name: "FK_accounts_commodities_ReportingCurrencyId",
                schema: "finances",
                table: "accounts",
                column: "ReportingCurrencyId",
                principalSchema: "finances",
                principalTable: "commodities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_accounts_commodities_ReportingCurrencyId",
                schema: "finances",
                table: "accounts");

            migrationBuilder.DropIndex(
                name: "IX_accounts_Kind",
                schema: "finances",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "finances",
                table: "accounts");

            migrationBuilder.RenameColumn(
                name: "ReportingCurrencyId",
                schema: "finances",
                table: "accounts",
                newName: "DefaultCommodityId");

            migrationBuilder.RenameIndex(
                name: "IX_accounts_ReportingCurrencyId",
                schema: "finances",
                table: "accounts",
                newName: "IX_accounts_DefaultCommodityId");

            migrationBuilder.AddColumn<bool>(
                name: "IsMultiPosition",
                schema: "finances",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                schema: "finances",
                table: "accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_AccountType",
                schema: "finances",
                table: "accounts",
                column: "AccountType",
                filter: "\"IsArchived\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_ParentId",
                schema: "finances",
                table: "accounts",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_accounts_accounts_ParentId",
                schema: "finances",
                table: "accounts",
                column: "ParentId",
                principalSchema: "finances",
                principalTable: "accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_accounts_commodities_DefaultCommodityId",
                schema: "finances",
                table: "accounts",
                column: "DefaultCommodityId",
                principalSchema: "finances",
                principalTable: "commodities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
