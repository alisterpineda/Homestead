using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Features.Documents.v1.PatchDocument;

public class PatchDocumentModel
{
    public string Name { get; set; } = string.Empty;
    public DateOnly? DocumentDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string Notes { get; set; } = string.Empty;
    public List<string> TagPaths { get; set; } = [];
    public List<Guid> PartyIds { get; set; } = [];
    public Guid? GroupId { get; set; }
}

public record PatchDocumentCommand : IRequest<PatchDocumentResponse>
{
    public Guid Id { get; init; }
    public required JsonPatchDocument<PatchDocumentModel> PatchDocument { get; init; }
}
