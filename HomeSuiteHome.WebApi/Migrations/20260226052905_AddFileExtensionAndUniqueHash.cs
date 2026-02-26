using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeSuiteHome.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddFileExtensionAndUniqueHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileExtension",
                schema: "documents",
                table: "documents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_documents_FileHash",
                schema: "documents",
                table: "documents",
                column: "FileHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_documents_FileHash",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "FileExtension",
                schema: "documents",
                table: "documents");
        }
    }
}
