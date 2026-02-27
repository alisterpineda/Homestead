using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Persistence;

public static class SearchVectorHelper
{
    public static async Task UpdateSearchVectorAsync(AppDbContext db, Guid documentId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE documents.documents
            SET "SearchVector" =
                setweight(to_tsvector('english', "Name"), 'A') ||
                setweight(to_tsvector('english', COALESCE(
                    (SELECT dv."ExtractedText"
                     FROM documents.document_versions dv
                     WHERE dv."DocumentId" = documents.documents."Id"
                     ORDER BY dv."VersionNumber" DESC
                     LIMIT 1), '')), 'B')
            WHERE "Id" = {documentId}
            """, ct);
    }
}
