using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AlignFinancesCommodityAndTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_commodity_prices_CommodityId_QuoteCurrencyId_PriceDate",
                schema: "finances",
                table: "commodity_prices");

            migrationBuilder.DropIndex(
                name: "IX_commodities_Symbol",
                schema: "finances",
                table: "commodities");

            migrationBuilder.DropIndex(
                name: "IX_account_transactions_AccountId_AssertedCommodityId_TxnDate",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.DropIndex(
                name: "IX_account_transactions_AccountId_TxnDate",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.DropIndex(
                name: "IX_account_transactions_TxnDate",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.DropColumn(
                name: "PriceDate",
                schema: "finances",
                table: "commodity_prices");

            migrationBuilder.DropColumn(
                name: "TxnDate",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByTransactionId",
                schema: "finances",
                table: "commodity_prices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PriceAt",
                schema: "finances",
                table: "commodity_prices",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "Exchange",
                schema: "finances",
                table: "commodities",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystemManaged",
                schema: "finances",
                table: "commodities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OccurredAt",
                schema: "finances",
                table: "account_transactions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "IX_commodity_prices_CommodityId_QuoteCurrencyId_PriceAt",
                schema: "finances",
                table: "commodity_prices",
                columns: new[] { "CommodityId", "QuoteCurrencyId", "PriceAt" },
                unique: true,
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_commodity_prices_CreatedByTransactionId",
                schema: "finances",
                table: "commodity_prices",
                column: "CreatedByTransactionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_commodity_prices_rate_positive",
                schema: "finances",
                table: "commodity_prices",
                sql: "\"Rate\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_commodity_prices_self_price",
                schema: "finances",
                table: "commodity_prices",
                sql: "\"CommodityId\" <> \"QuoteCurrencyId\"");

            migrationBuilder.CreateIndex(
                name: "IX_commodities_Symbol_currency",
                schema: "finances",
                table: "commodities",
                column: "Symbol",
                unique: true,
                filter: "\"Exchange\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_commodities_Symbol_Exchange_security",
                schema: "finances",
                table: "commodities",
                columns: new[] { "Symbol", "Exchange" },
                unique: true,
                filter: "\"Exchange\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_commodities_exchange_matches_kind",
                schema: "finances",
                table: "commodities",
                sql: "(\"Kind\" = 'Currency' AND \"Exchange\" IS NULL) OR (\"Kind\" = 'Security' AND \"Exchange\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_AccountId_AssertedCommodityId_Occurred~",
                schema: "finances",
                table: "account_transactions",
                columns: new[] { "AccountId", "AssertedCommodityId", "OccurredAt" },
                unique: true,
                filter: "\"Type\" = 'BalanceEntry'");

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_AccountId_OccurredAt",
                schema: "finances",
                table: "account_transactions",
                columns: new[] { "AccountId", "OccurredAt" },
                filter: "\"Type\" = 'BalanceEntry'");

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_OccurredAt",
                schema: "finances",
                table: "account_transactions",
                column: "OccurredAt");

            migrationBuilder.AddForeignKey(
                name: "FK_commodity_prices_account_transactions_CreatedByTransactionId",
                schema: "finances",
                table: "commodity_prices",
                column: "CreatedByTransactionId",
                principalSchema: "finances",
                principalTable: "account_transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_commodity_prices_account_transactions_CreatedByTransactionId",
                schema: "finances",
                table: "commodity_prices");

            migrationBuilder.DropIndex(
                name: "IX_commodity_prices_CommodityId_QuoteCurrencyId_PriceAt",
                schema: "finances",
                table: "commodity_prices");

            migrationBuilder.DropIndex(
                name: "IX_commodity_prices_CreatedByTransactionId",
                schema: "finances",
                table: "commodity_prices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_commodity_prices_rate_positive",
                schema: "finances",
                table: "commodity_prices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_commodity_prices_self_price",
                schema: "finances",
                table: "commodity_prices");

            migrationBuilder.DropIndex(
                name: "IX_commodities_Symbol_currency",
                schema: "finances",
                table: "commodities");

            migrationBuilder.DropIndex(
                name: "IX_commodities_Symbol_Exchange_security",
                schema: "finances",
                table: "commodities");

            migrationBuilder.DropCheckConstraint(
                name: "CK_commodities_exchange_matches_kind",
                schema: "finances",
                table: "commodities");

            migrationBuilder.DropIndex(
                name: "IX_account_transactions_AccountId_AssertedCommodityId_Occurred~",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.DropIndex(
                name: "IX_account_transactions_AccountId_OccurredAt",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.DropIndex(
                name: "IX_account_transactions_OccurredAt",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.DropColumn(
                name: "CreatedByTransactionId",
                schema: "finances",
                table: "commodity_prices");

            migrationBuilder.DropColumn(
                name: "PriceAt",
                schema: "finances",
                table: "commodity_prices");

            migrationBuilder.DropColumn(
                name: "Exchange",
                schema: "finances",
                table: "commodities");

            migrationBuilder.DropColumn(
                name: "IsSystemManaged",
                schema: "finances",
                table: "commodities");

            migrationBuilder.DropColumn(
                name: "OccurredAt",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.AddColumn<DateOnly>(
                name: "PriceDate",
                schema: "finances",
                table: "commodity_prices",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "TxnDate",
                schema: "finances",
                table: "account_transactions",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.CreateIndex(
                name: "IX_commodity_prices_CommodityId_QuoteCurrencyId_PriceDate",
                schema: "finances",
                table: "commodity_prices",
                columns: new[] { "CommodityId", "QuoteCurrencyId", "PriceDate" },
                unique: true,
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_commodities_Symbol",
                schema: "finances",
                table: "commodities",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_AccountId_AssertedCommodityId_TxnDate",
                schema: "finances",
                table: "account_transactions",
                columns: new[] { "AccountId", "AssertedCommodityId", "TxnDate" },
                unique: true,
                filter: "\"Type\" = 'BalanceEntry'");

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_AccountId_TxnDate",
                schema: "finances",
                table: "account_transactions",
                columns: new[] { "AccountId", "TxnDate" },
                filter: "\"Type\" = 'BalanceEntry'");

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_TxnDate",
                schema: "finances",
                table: "account_transactions",
                column: "TxnDate");
        }
    }
}
