using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeSuiteHome.WebApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentExtractedText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractedText",
                schema: "documents",
                table: "documents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtractedText",
                schema: "documents",
                table: "documents");
        }
    }
}
