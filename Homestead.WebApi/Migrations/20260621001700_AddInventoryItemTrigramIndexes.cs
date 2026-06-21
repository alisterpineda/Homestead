using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryItemTrigramIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backs both the word_similarity fuzzy path and the ILIKE substring match on Name.
            // pg_trgm is already enabled globally (AppDbContext.HasPostgresExtension("pg_trgm")).
            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_items_Name_trgm""
                ON inventory.items
                USING GIN (""Name"" gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS inventory.""IX_items_Name_trgm"";");
        }
    }
}
