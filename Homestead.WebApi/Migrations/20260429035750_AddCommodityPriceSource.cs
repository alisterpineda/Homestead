using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCommodityPriceSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_commodity_prices_CommodityId_QuoteCurrencyId_PriceAt",
                schema: "finances",
                table: "commodity_prices");

            // Add column nullable so existing rows can be backfilled before NOT NULL is enforced.
            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "finances",
                table: "commodity_prices",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE finances.commodity_prices "
                + "SET \"Source\" = CASE WHEN \"CreatedByTransactionId\" IS NULL THEN 'Manual' ELSE 'Transaction' END;");

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                schema: "finances",
                table: "commodity_prices",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "commodity_price_source_settings",
                schema: "finances",
                columns: table => new
                {
                    Source = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Config = table.Column<string>(type: "jsonb", nullable: true),
                    PairAllowList = table.Column<string>(type: "jsonb", nullable: true),
                    LastImportAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastImportStatus = table.Column<string>(type: "text", nullable: true),
                    LastImportError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commodity_price_source_settings", x => x.Source);
                    table.CheckConstraint("CK_commodity_price_source_settings_source_kind", "\"Source\" NOT IN ('Transaction', 'Manual')");
                    table.ForeignKey(
                        name: "FK_commodity_price_source_settings_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_commodity_price_source_settings_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_commodity_prices_CommodityId_QuoteCurrencyId_PriceAt_Source",
                schema: "finances",
                table: "commodity_prices",
                columns: new[] { "CommodityId", "QuoteCurrencyId", "PriceAt", "Source" },
                unique: true,
                descending: new[] { false, false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_commodity_price_source_settings_CreatedByUserId",
                schema: "finances",
                table: "commodity_price_source_settings",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_commodity_price_source_settings_UpdatedByUserId",
                schema: "finances",
                table: "commodity_price_source_settings",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commodity_price_source_settings",
                schema: "finances");

            migrationBuilder.DropIndex(
                name: "IX_commodity_prices_CommodityId_QuoteCurrencyId_PriceAt_Source",
                schema: "finances",
                table: "commodity_prices");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "finances",
                table: "commodity_prices");

            migrationBuilder.CreateIndex(
                name: "IX_commodity_prices_CommodityId_QuoteCurrencyId_PriceAt",
                schema: "finances",
                table: "commodity_prices",
                columns: new[] { "CommodityId", "QuoteCurrencyId", "PriceAt" },
                unique: true,
                descending: new[] { false, false, true });
        }
    }
}
