using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class MoveTagsToDocumentsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "tags",
                schema: "tags",
                newName: "tags",
                newSchema: "documents");

            migrationBuilder.Sql("DROP SCHEMA IF EXISTS tags;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tags");

            migrationBuilder.RenameTable(
                name: "tags",
                schema: "documents",
                newName: "tags",
                newSchema: "tags");
        }
    }
}
