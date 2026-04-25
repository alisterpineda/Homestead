using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class ShiftFinancesToPerCommodityBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_account_transactions_commodities_CurrencyId",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.DropIndex(
                name: "IX_account_transactions_CurrencyId",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.DropColumn(
                name: "Value",
                schema: "finances",
                table: "account_transaction_splits");

            migrationBuilder.AddColumn<Guid>(
                name: "UnitPriceCommodityId",
                schema: "finances",
                table: "account_transaction_splits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPriceRate",
                schema: "finances",
                table: "account_transaction_splits",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_transaction_splits_UnitPriceCommodityId",
                schema: "finances",
                table: "account_transaction_splits",
                column: "UnitPriceCommodityId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_transaction_splits_unit_price",
                schema: "finances",
                table: "account_transaction_splits",
                sql: "(\"UnitPriceRate\" IS NULL AND \"UnitPriceCommodityId\" IS NULL) OR (\"UnitPriceRate\" IS NOT NULL AND \"UnitPriceCommodityId\" IS NOT NULL AND \"UnitPriceRate\" > 0 AND \"UnitPriceCommodityId\" <> \"CommodityId\")");

            migrationBuilder.AddForeignKey(
                name: "FK_account_transaction_splits_commodities_UnitPriceCommodityId",
                schema: "finances",
                table: "account_transaction_splits",
                column: "UnitPriceCommodityId",
                principalSchema: "finances",
                principalTable: "commodities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_account_transaction_splits_commodities_UnitPriceCommodityId",
                schema: "finances",
                table: "account_transaction_splits");

            migrationBuilder.DropIndex(
                name: "IX_account_transaction_splits_UnitPriceCommodityId",
                schema: "finances",
                table: "account_transaction_splits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_transaction_splits_unit_price",
                schema: "finances",
                table: "account_transaction_splits");

            migrationBuilder.DropColumn(
                name: "UnitPriceCommodityId",
                schema: "finances",
                table: "account_transaction_splits");

            migrationBuilder.DropColumn(
                name: "UnitPriceRate",
                schema: "finances",
                table: "account_transaction_splits");

            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyId",
                schema: "finances",
                table: "account_transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "Value",
                schema: "finances",
                table: "account_transaction_splits",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_CurrencyId",
                schema: "finances",
                table: "account_transactions",
                column: "CurrencyId");

            migrationBuilder.AddForeignKey(
                name: "FK_account_transactions_commodities_CurrencyId",
                schema: "finances",
                table: "account_transactions",
                column: "CurrencyId",
                principalSchema: "finances",
                principalTable: "commodities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
