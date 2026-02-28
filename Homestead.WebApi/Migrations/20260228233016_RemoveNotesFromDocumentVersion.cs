using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveNotesFromDocumentVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                schema: "documents",
                table: "document_versions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "documents",
                table: "document_versions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }
    }
}
