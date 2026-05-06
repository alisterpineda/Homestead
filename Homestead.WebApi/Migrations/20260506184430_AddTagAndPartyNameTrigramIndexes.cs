using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTagAndPartyNameTrigramIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_tags_Name_trgm""
                ON documents.tags
                USING GIN (""Name"" gin_trgm_ops);");

            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_parties_Name_trgm""
                ON documents.parties
                USING GIN (""Name"" gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS documents.""IX_tags_Name_trgm"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS documents.""IX_parties_Name_trgm"";");
        }
    }
}
