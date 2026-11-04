using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Features.Suggestions;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Homestead.WebApi.Modules.Documents.Features.Versions;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocument;

public class GetDocumentHandler(AppDbContext db)
{
    public async Task<GetDocumentResponse?> HandleAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.CreatedAt,
                d.UpdatedAt,
                d.UpdatedByUserId,
                UpdatedByUserName = d.UpdatedByUser != null ? d.UpdatedByUser.DisplayName : null,
                d.DocumentDate,
                d.ExpiryDate,
                d.Notes,
                d.CreatedByUserId,
                CreatedByUserName = d.CreatedByUser != null ? d.CreatedByUser.DisplayName : null,
                d.OwnerUserId,
                OwnerUserName = d.OwnerUser != null ? d.OwnerUser.DisplayName : null,
                d.GroupId,
                GroupName = d.Group != null ? d.Group.Name : null,
                d.Summary,
                LatestVersion = d.Versions
                    .OrderByDescending(r => r.VersionNumber)
                    .Select(r => new
                    {
                        r.ContentType,
                        r.FileSize,
                        r.FileHash,
                        r.FileExtension,
                        r.ExtractedText,
                        r.TextExtractionMetadata
                    })
                    .FirstOrDefault(),
                Versions = d.Versions
                    .OrderByDescending(r => r.VersionNumber)
                    .Select(r => new GetDocumentVersionDto
                    {
                        Id = r.Id,
                        VersionNumber = r.VersionNumber,
                        Label = r.Label,
                        ContentType = r.ContentType,
                        FileSize = r.FileSize,
                        FileExtension = r.FileExtension,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt
                    })
                    .ToList(),
                VersionCount = d.Versions.Count,
                Tags = d.DocumentTags.Select(dt => new GetDocumentTagDto
                {
                    Id = dt.Tag.Id,
                    Name = dt.Tag.Name,
                    Path = dt.Tag.Path,
                    Color = dt.Tag.Color,
                    EffectiveColor = dt.Tag.EffectiveColor,
                    Description = dt.Tag.Description
                }).ToList(),
                Parties = d.DocumentParties.Select(dp => new GetDocumentPartyDto
                {
                    Id = dp.Party.Id,
                    Name = dp.Party.Name,
                    Description = dp.Party.Description
                }).ToList(),
                Suggestions = d.Suggestions
                    .Where(s => s.Status == DocumentSuggestionStatus.Pending)
                    .Select(s => new DocumentSuggestionDto
                    {
                        Id = s.Id,
                        Field = s.Field,
                        Status = s.Status,
                        SuggestedValue = s.SuggestedValue,
                        OriginalValue = s.OriginalValue,
                        CreatedAt = s.CreatedAt,
                        ResolvedAt = s.ResolvedAt,
                    }).ToList()
            })
            .FirstOrDefaultAsync(ct);

        // Materialized before the final map (rather than projecting straight into the response in SQL):
        // the provenance blob has to be deserialized and flattened in memory. Same columns, same query.
        if (d?.LatestVersion is null)
            return null;

        return new GetDocumentResponse
        {
            Id = d.Id,
            Name = d.Name,
            ContentType = d.LatestVersion.ContentType,
            FileSize = d.LatestVersion.FileSize,
            FileHash = d.LatestVersion.FileHash,
            FileExtension = d.LatestVersion.FileExtension,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt,
            UpdatedByUserId = d.UpdatedByUserId,
            UpdatedByUserName = d.UpdatedByUserName,
            DocumentDate = d.DocumentDate,
            ExpiryDate = d.ExpiryDate,
            Notes = d.Notes,
            CreatedByUserId = d.CreatedByUserId,
            CreatedByUserName = d.CreatedByUserName,
            OwnerUserId = d.OwnerUserId,
            OwnerUserName = d.OwnerUserName,
            GroupId = d.GroupId,
            GroupName = d.GroupName,
            Summary = d.Summary,
            ExtractedText = d.LatestVersion.ExtractedText,
            TextExtraction = await TextExtractionProvenanceReader.ReadAsync(db, d.LatestVersion.TextExtractionMetadata, ct),
            VersionCount = d.VersionCount,
            Versions = d.Versions,
            Tags = d.Tags,
            Parties = d.Parties,
            Suggestions = d.Suggestions,
        };
    }
}
