using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeSuiteHome.WebApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MigrateFileNameToNameAndDropFileName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """UPDATE documents."documents" SET "Name" = "FileName" WHERE "FileName" IS NOT NULL""");

            migrationBuilder.DropColumn(
                name: "FileName",
                schema: "documents",
                table: "documents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileName",
                schema: "documents",
                table: "documents",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
