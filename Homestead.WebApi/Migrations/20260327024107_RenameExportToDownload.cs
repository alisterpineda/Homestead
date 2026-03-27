using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class RenameExportToDownload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """UPDATE processing.jobs SET "Type" = 'DownloadDocuments' WHERE "Type" = 'ExportDocuments'""");
            migrationBuilder.Sql(
                """UPDATE processing.jobs SET "EntityType" = 'Download' WHERE "EntityType" = 'Export'""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """UPDATE processing.jobs SET "Type" = 'ExportDocuments' WHERE "Type" = 'DownloadDocuments'""");
            migrationBuilder.Sql(
                """UPDATE processing.jobs SET "EntityType" = 'Export' WHERE "EntityType" = 'Download'""");
        }
    }
}
