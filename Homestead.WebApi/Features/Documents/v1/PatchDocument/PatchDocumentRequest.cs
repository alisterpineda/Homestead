using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Features.Documents.v1.PatchDocument;

public class PatchDocumentModel
{
    public string Name { get; set; } = string.Empty;
    public DateOnly? DocumentDate { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public List<string> TagPaths { get; set; } = [];
    public List<Guid> PartyIds { get; set; } = [];
}

public record PatchDocumentCommand
{
    public Guid Id { get; init; }
    public required JsonPatchDocument<PatchDocumentModel> PatchDocument { get; init; }
}
