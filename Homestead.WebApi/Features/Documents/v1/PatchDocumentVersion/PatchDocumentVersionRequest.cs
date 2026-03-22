using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Features.Documents.v1.PatchDocumentVersion;

public class PatchDocumentVersionModel
{
    public string? Label { get; set; }
    public string? ExtractedText { get; set; }
}

public record PatchDocumentVersionCommand : IRequest<PatchDocumentVersionResponse>
{
    public Guid DocumentId { get; init; }
    public Guid Id { get; init; }
    public required JsonPatchDocument<PatchDocumentVersionModel> PatchDocument { get; init; }
}
