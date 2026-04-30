using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class RenameLastImportErrorToLastImportMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LastImportError",
                schema: "finances",
                table: "commodity_price_source_settings",
                newName: "LastImportMessage");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LastImportMessage",
                schema: "finances",
                table: "commodity_price_source_settings",
                newName: "LastImportError");
        }
    }
}
