using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeSuiteHome.WebApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameDocumentProcessToUploadDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """UPDATE processing.jobs SET "Type" = 'UploadDocument' WHERE "Type" = 'DocumentProcess'""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """UPDATE processing.jobs SET "Type" = 'DocumentProcess' WHERE "Type" = 'UploadDocument'""");
        }
    }
}
