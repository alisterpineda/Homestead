using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Services;

public record DocumentClassificationOutput(
    ClassificationResult Classification,
    List<TagInfo> AvailableTags,
    Guid ProviderId);

public interface IDocumentClassifier
{
    Task<DocumentClassificationOutput?> ClassifyAsync(
        Guid documentId, string documentName, Guid? aiProviderId, CancellationToken ct);
}

public class DocumentClassifier(
    AppDbContext db,
    IAiProviderResolver providerResolver,
    IAiClassificationService classificationService) : IDocumentClassifier
{
    public async Task<DocumentClassificationOutput?> ClassifyAsync(
        Guid documentId, string documentName, Guid? aiProviderId, CancellationToken ct)
    {
        var provider = await providerResolver.ResolveAsync(aiProviderId, ct);
        if (provider is null)
            return null;

        var latestVersion = await db.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        var allTags = await db.Tags
            .AsNoTracking()
            .Select(t => new TagInfo(t.Id, t.Name, t.Path))
            .ToListAsync(ct);

        var result = await classificationService.ClassifyAsync(
            provider, documentName, latestVersion?.ExtractedText, allTags, ct);

        if (result is null)
            return null;

        return new DocumentClassificationOutput(result, allTags, provider.Id);
    }
}
