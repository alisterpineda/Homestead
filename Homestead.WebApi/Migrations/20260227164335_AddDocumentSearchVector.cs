using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                schema: "documents",
                table: "documents",
                type: "tsvector",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_documents_SearchVector",
                schema: "documents",
                table: "documents",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            // Backfill search vectors for existing documents
            migrationBuilder.Sql(
                """
                UPDATE documents.documents d
                SET "SearchVector" =
                    setweight(to_tsvector('english', d."Name"), 'A') ||
                    setweight(to_tsvector('english', COALESCE(
                        (SELECT dv."ExtractedText"
                         FROM documents.document_versions dv
                         WHERE dv."DocumentId" = d."Id"
                         ORDER BY dv."VersionNumber" DESC
                         LIMIT 1), '')), 'B')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_documents_SearchVector",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                schema: "documents",
                table: "documents");
        }
    }
}
