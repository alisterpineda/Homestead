using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCommodityDisplayPrecisionRangeCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_commodities_display_precision_range",
                schema: "finances",
                table: "commodities",
                sql: "\"DisplayPrecision\" >= 0 AND \"DisplayPrecision\" <= 10");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_commodities_display_precision_range",
                schema: "finances",
                table: "commodities");
        }
    }
}
