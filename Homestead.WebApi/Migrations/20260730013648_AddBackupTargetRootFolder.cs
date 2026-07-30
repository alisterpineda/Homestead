using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupTargetRootFolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RootFolderPath",
                schema: "backup",
                table: "target_state",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RootFolderWebViewLink",
                schema: "backup",
                table: "target_state",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RootFolderPath",
                schema: "backup",
                table: "target_state");

            migrationBuilder.DropColumn(
                name: "RootFolderWebViewLink",
                schema: "backup",
                table: "target_state");
        }
    }
}
