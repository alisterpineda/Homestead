using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finances");

            migrationBuilder.CreateTable(
                name: "commodities",
                schema: "finances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DisplayPrecision = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commodities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_commodities_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_commodities_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "finances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AccountType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultCommodityId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsMultiPosition = table.Column<bool>(type: "boolean", nullable: false),
                    Institution = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounts_accounts_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "finances",
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounts_commodities_DefaultCommodityId",
                        column: x => x.DefaultCommodityId,
                        principalSchema: "finances",
                        principalTable: "commodities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounts_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_accounts_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "commodity_prices",
                schema: "finances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommodityId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteCurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commodity_prices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_commodity_prices_commodities_CommodityId",
                        column: x => x.CommodityId,
                        principalSchema: "finances",
                        principalTable: "commodities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_commodity_prices_commodities_QuoteCurrencyId",
                        column: x => x.QuoteCurrencyId,
                        principalSchema: "finances",
                        principalTable: "commodities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_commodity_prices_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_commodity_prices_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "account_transactions",
                schema: "finances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TxnDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssertedCommodityId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssertedQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    Note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_account_transactions_accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "finances",
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_transactions_commodities_AssertedCommodityId",
                        column: x => x.AssertedCommodityId,
                        principalSchema: "finances",
                        principalTable: "commodities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_transactions_commodities_CurrencyId",
                        column: x => x.CurrencyId,
                        principalSchema: "finances",
                        principalTable: "commodities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_transactions_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_account_transactions_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "account_transaction_splits",
                schema: "finances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommodityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: false),
                    Memo = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_transaction_splits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_account_transaction_splits_account_transactions_Transaction~",
                        column: x => x.TransactionId,
                        principalSchema: "finances",
                        principalTable: "account_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_account_transaction_splits_accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "finances",
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_transaction_splits_commodities_CommodityId",
                        column: x => x.CommodityId,
                        principalSchema: "finances",
                        principalTable: "commodities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_transaction_splits_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_account_transaction_splits_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_transaction_splits_AccountId_CommodityId",
                schema: "finances",
                table: "account_transaction_splits",
                columns: new[] { "AccountId", "CommodityId" });

            migrationBuilder.CreateIndex(
                name: "IX_account_transaction_splits_CommodityId",
                schema: "finances",
                table: "account_transaction_splits",
                column: "CommodityId");

            migrationBuilder.CreateIndex(
                name: "IX_account_transaction_splits_CreatedByUserId",
                schema: "finances",
                table: "account_transaction_splits",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_account_transaction_splits_TransactionId",
                schema: "finances",
                table: "account_transaction_splits",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_account_transaction_splits_UpdatedByUserId",
                schema: "finances",
                table: "account_transaction_splits",
                column: "UpdatedByUserId");

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
                name: "IX_account_transactions_AssertedCommodityId",
                schema: "finances",
                table: "account_transactions",
                column: "AssertedCommodityId");

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_CreatedByUserId",
                schema: "finances",
                table: "account_transactions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_CurrencyId",
                schema: "finances",
                table: "account_transactions",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_TxnDate",
                schema: "finances",
                table: "account_transactions",
                column: "TxnDate");

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_UpdatedByUserId",
                schema: "finances",
                table: "account_transactions",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_AccountType",
                schema: "finances",
                table: "accounts",
                column: "AccountType",
                filter: "\"IsArchived\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_CreatedByUserId",
                schema: "finances",
                table: "accounts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_DefaultCommodityId",
                schema: "finances",
                table: "accounts",
                column: "DefaultCommodityId");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_ParentId",
                schema: "finances",
                table: "accounts",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_UpdatedByUserId",
                schema: "finances",
                table: "accounts",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_commodities_CreatedByUserId",
                schema: "finances",
                table: "commodities",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_commodities_Symbol",
                schema: "finances",
                table: "commodities",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_commodities_UpdatedByUserId",
                schema: "finances",
                table: "commodities",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_commodity_prices_CommodityId_QuoteCurrencyId_PriceDate",
                schema: "finances",
                table: "commodity_prices",
                columns: new[] { "CommodityId", "QuoteCurrencyId", "PriceDate" },
                unique: true,
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_commodity_prices_CreatedByUserId",
                schema: "finances",
                table: "commodity_prices",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_commodity_prices_QuoteCurrencyId",
                schema: "finances",
                table: "commodity_prices",
                column: "QuoteCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_commodity_prices_UpdatedByUserId",
                schema: "finances",
                table: "commodity_prices",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_transaction_splits",
                schema: "finances");

            migrationBuilder.DropTable(
                name: "commodity_prices",
                schema: "finances");

            migrationBuilder.DropTable(
                name: "account_transactions",
                schema: "finances");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "finances");

            migrationBuilder.DropTable(
                name: "commodities",
                schema: "finances");
        }
    }
}
