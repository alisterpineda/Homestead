using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <summary>
    /// Accent folding for document search. The stored search vector (AddSearchVectorTriggers) is rebuilt from
    /// <c>unaccent</c>-folded name, text and summary, so "factură" and "Factura" index to the same lexeme; the query
    /// side folds its terms the same way (DocumentSearchTerms). Only the two trigger functions change — the triggers
    /// themselves, the column and its GIN index stay — and every existing vector is recomputed once.
    /// </summary>
    public partial class AddUnaccentSearchFolding : Migration
    {
        // The vector expression, folded. unaccent() is stable, not immutable, which a trigger body may call freely
        // (only an index expression or a generated column needs immutability). Schema-qualified: the extension lands
        // in public, the trigger functions live in documents.
        private const string FoldedVector =
            """
                setweight(to_tsvector('english', public.unaccent(COALESCE({0}."Name", ''))), 'A') ||
                setweight(to_tsvector('english', public.unaccent(COALESCE(
                    (SELECT dv."ExtractedText"
                     FROM documents.document_versions dv
                     WHERE dv."DocumentId" = {0}."Id"
                     ORDER BY dv."VersionNumber" DESC
                     LIMIT 1), ''))), 'B') ||
                setweight(to_tsvector('english', public.unaccent(COALESCE({0}."Summary", ''))), 'C')
            """;

        private const string PlainVector =
            """
                setweight(to_tsvector('english', COALESCE({0}."Name", '')), 'A') ||
                setweight(to_tsvector('english', COALESCE(
                    (SELECT dv."ExtractedText"
                     FROM documents.document_versions dv
                     WHERE dv."DocumentId" = {0}."Id"
                     ORDER BY dv."VersionNumber" DESC
                     LIMIT 1), '')), 'B') ||
                setweight(to_tsvector('english', COALESCE({0}."Summary", '')), 'C')
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            ReplaceVectorFunctions(migrationBuilder, FoldedVector);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ReplaceVectorFunctions(migrationBuilder, PlainVector);

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,");
        }

        // CREATE OR REPLACE keeps the existing triggers bound; the backfill brings every row to the new definition
        // (a SearchVector-only UPDATE does not fire the documents trigger).
        private static void ReplaceVectorFunctions(MigrationBuilder migrationBuilder, string vector)
        {
            migrationBuilder.Sql(
                $"""
                CREATE OR REPLACE FUNCTION documents.documents_search_vector_update()
                RETURNS trigger AS $$
                BEGIN
                    NEW."SearchVector" :=
                {string.Format(vector, "NEW")};
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                $"""
                CREATE OR REPLACE FUNCTION documents.document_versions_search_vector_update()
                RETURNS trigger AS $$
                DECLARE
                    doc_id uuid := COALESCE(NEW."DocumentId", OLD."DocumentId");
                BEGIN
                    UPDATE documents.documents d
                    SET "SearchVector" =
                {string.Format(vector, "d")}
                    WHERE d."Id" = doc_id;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                $"""
                UPDATE documents.documents d
                SET "SearchVector" =
                {string.Format(vector, "d")};
                """);
        }
    }
}
