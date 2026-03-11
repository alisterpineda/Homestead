using Homestead.WebApi.Domains.Documents;

namespace Homestead.WebApi.Features.Documents.v1;

public interface IDocumentMetadataService
{
    Task<List<Tag>> ResolveTagPathsAsync(List<string> tagPaths, CancellationToken ct);
    Task ValidatePartyIdsAsync(List<Guid> partyIds, CancellationToken ct);
    void SetTags(Document document, List<Tag> tags);
    void AddTags(Document document, List<Tag> tags);
    void SetParties(Document document, List<Guid> partyIds);
    void AddParties(Document document, List<Guid> partyIds);
    void RemoveTags(Document document, List<string> tagPaths);
    void RemoveParties(Document document, List<Guid> partyIds);
}
