using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class RenameAppSettingsModuleToKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Module",
                table: "app_settings",
                schema: "settings",
                newName: "Key");

            migrationBuilder.Sql("UPDATE settings.app_settings SET \"Key\" = LOWER(\"Key\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE settings.app_settings SET \"Key\" = INITCAP(\"Key\")");

            migrationBuilder.RenameColumn(
                name: "Key",
                table: "app_settings",
                schema: "settings",
                newName: "Module");
        }
    }
}
