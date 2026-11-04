using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.GetDocumentVersion;

public class GetDocumentVersionHandler(AppDbContext db)
{
    public async Task<GetDocumentVersionResponse?> HandleAsync(Guid documentId, Guid id, CancellationToken ct)
    {
        // Projected to an intermediate first: the provenance blob has to be deserialized and flattened in
        // memory, which EF cannot translate into the query.
        var row = await db.DocumentVersions
            .AsNoTracking()
            .Where(r => r.DocumentId == documentId && r.Id == id)
            .Select(r => new
            {
                r.Id,
                r.VersionNumber,
                r.Label,
                r.ContentType,
                r.FileSize,
                r.FileHash,
                r.FileExtension,
                r.ExtractedText,
                r.TextExtractionMetadata,
                r.CreatedAt,
                r.UpdatedAt
            })
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return null;

        return new GetDocumentVersionResponse
        {
            Id = row.Id,
            VersionNumber = row.VersionNumber,
            Label = row.Label,
            ContentType = row.ContentType,
            FileSize = row.FileSize,
            FileHash = row.FileHash,
            FileExtension = row.FileExtension,
            ExtractedText = row.ExtractedText,
            TextExtraction = await TextExtractionProvenanceReader.ReadAsync(db, row.TextExtractionMetadata, ct),
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }
}
