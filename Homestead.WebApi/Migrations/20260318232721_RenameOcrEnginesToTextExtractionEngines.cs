using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class RenameOcrEnginesToTextExtractionEngines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "ocr_engines",
                schema: "settings",
                newName: "text_extraction_engines",
                newSchema: "settings");

            migrationBuilder.RenameIndex(
                name: "IX_ocr_engines_CreatedByUserId",
                schema: "settings",
                table: "text_extraction_engines",
                newName: "IX_text_extraction_engines_CreatedByUserId");

            migrationBuilder.RenameIndex(
                name: "IX_ocr_engines_Name",
                schema: "settings",
                table: "text_extraction_engines",
                newName: "IX_text_extraction_engines_Name");

            // Rename constraints
            migrationBuilder.Sql(
                """ALTER TABLE settings.text_extraction_engines RENAME CONSTRAINT "PK_ocr_engines" TO "PK_text_extraction_engines" """);
            migrationBuilder.Sql(
                """ALTER TABLE settings.text_extraction_engines RENAME CONSTRAINT "FK_ocr_engines_app_users_CreatedByUserId" TO "FK_text_extraction_engines_app_users_CreatedByUserId" """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """ALTER TABLE settings.text_extraction_engines RENAME CONSTRAINT "FK_text_extraction_engines_app_users_CreatedByUserId" TO "FK_ocr_engines_app_users_CreatedByUserId" """);
            migrationBuilder.Sql(
                """ALTER TABLE settings.text_extraction_engines RENAME CONSTRAINT "PK_text_extraction_engines" TO "PK_ocr_engines" """);

            migrationBuilder.RenameIndex(
                name: "IX_text_extraction_engines_Name",
                schema: "settings",
                table: "text_extraction_engines",
                newName: "IX_ocr_engines_Name");

            migrationBuilder.RenameIndex(
                name: "IX_text_extraction_engines_CreatedByUserId",
                schema: "settings",
                table: "text_extraction_engines",
                newName: "IX_ocr_engines_CreatedByUserId");

            migrationBuilder.RenameTable(
                name: "text_extraction_engines",
                schema: "settings",
                newName: "ocr_engines",
                newSchema: "settings");
        }
    }
}
