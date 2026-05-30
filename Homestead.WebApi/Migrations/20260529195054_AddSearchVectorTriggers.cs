using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchVectorTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Move SearchVector maintenance from app code (the deleted SearchVectorUpdater) into the
            // database. The vector spans two tables — Name/Summary live on documents, the latest
            // version's ExtractedText lives on document_versions — so a GENERATED column is not
            // sufficient; two triggers keep it in sync. Weights: A = Name, B = latest ExtractedText,
            // C = Summary (matches the former SearchVectorUpdater).

            // documents: recompute NEW.SearchVector in place whenever Name or Summary changes (and on
            // insert). BEFORE so no extra UPDATE is needed. "UPDATE OF Name, Summary" means the
            // SearchVector-only UPDATE issued by the document_versions trigger below does NOT re-fire
            // this trigger — no recursion.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION documents.documents_search_vector_update()
                RETURNS trigger AS $$
                BEGIN
                    NEW."SearchVector" :=
                        setweight(to_tsvector('english', COALESCE(NEW."Name", '')), 'A') ||
                        setweight(to_tsvector('english', COALESCE(
                            (SELECT dv."ExtractedText"
                             FROM documents.document_versions dv
                             WHERE dv."DocumentId" = NEW."Id"
                             ORDER BY dv."VersionNumber" DESC
                             LIMIT 1), '')), 'B') ||
                        setweight(to_tsvector('english', COALESCE(NEW."Summary", '')), 'C');
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER documents_search_vector_trigger
                BEFORE INSERT OR UPDATE OF "Name", "Summary"
                ON documents.documents
                FOR EACH ROW
                EXECUTE FUNCTION documents.documents_search_vector_update();
                """);

            // document_versions: any change to a version's ExtractedText, or adding/removing a version
            // (which can change which version is latest), re-derives the parent document's vector.
            // AFTER so the changed/added/removed row is already visible to the latest-version subquery.
            // When the parent is mid-cascade-delete, the UPDATE simply matches no rows.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION documents.document_versions_search_vector_update()
                RETURNS trigger AS $$
                DECLARE
                    doc_id uuid := COALESCE(NEW."DocumentId", OLD."DocumentId");
                BEGIN
                    UPDATE documents.documents d
                    SET "SearchVector" =
                        setweight(to_tsvector('english', COALESCE(d."Name", '')), 'A') ||
                        setweight(to_tsvector('english', COALESCE(
                            (SELECT dv."ExtractedText"
                             FROM documents.document_versions dv
                             WHERE dv."DocumentId" = d."Id"
                             ORDER BY dv."VersionNumber" DESC
                             LIMIT 1), '')), 'B') ||
                        setweight(to_tsvector('english', COALESCE(d."Summary", '')), 'C')
                    WHERE d."Id" = doc_id;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER document_versions_search_vector_trigger
                AFTER INSERT OR DELETE OR UPDATE OF "ExtractedText", "VersionNumber"
                ON documents.document_versions
                FOR EACH ROW
                EXECUTE FUNCTION documents.document_versions_search_vector_update();
                """);

            // Backfill existing rows so the DB-owned vector matches the trigger definition exactly
            // (SearchVector-only UPDATE does not fire the documents trigger).
            migrationBuilder.Sql(
                """
                UPDATE documents.documents d
                SET "SearchVector" =
                    setweight(to_tsvector('english', COALESCE(d."Name", '')), 'A') ||
                    setweight(to_tsvector('english', COALESCE(
                        (SELECT dv."ExtractedText"
                         FROM documents.document_versions dv
                         WHERE dv."DocumentId" = d."Id"
                         ORDER BY dv."VersionNumber" DESC
                         LIMIT 1), '')), 'B') ||
                    setweight(to_tsvector('english', COALESCE(d."Summary", '')), 'C');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS document_versions_search_vector_trigger ON documents.document_versions;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS documents_search_vector_trigger ON documents.documents;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS documents.document_versions_search_vector_update();");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS documents.documents_search_vector_update();");
        }
    }
}
