using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIsAppliedToDocumentSuggestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsApplied",
                schema: "documents",
                table: "document_suggestions",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsApplied",
                schema: "documents",
                table: "document_suggestions");
        }
    }
}
