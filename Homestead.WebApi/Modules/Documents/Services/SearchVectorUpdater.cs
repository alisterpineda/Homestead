using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Services;

public class SearchVectorUpdater(AppDbContext db) : ISearchVectorUpdater
{
    public async Task UpdateSearchVectorAsync(Guid documentId, CancellationToken ct)
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
                     LIMIT 1), '')), 'B') ||
                setweight(to_tsvector('english', COALESCE("Summary", '')), 'C')
            WHERE "Id" = {documentId}
            """, ct);
    }
}
