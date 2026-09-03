using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class RemovePriceSourceDynamicConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Config",
                schema: "finances",
                table: "commodity_price_source_settings");

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                schema: "finances",
                table: "commodity_price_source_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Config",
                schema: "finances",
                table: "commodity_price_source_settings",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                schema: "finances",
                table: "commodity_price_source_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
