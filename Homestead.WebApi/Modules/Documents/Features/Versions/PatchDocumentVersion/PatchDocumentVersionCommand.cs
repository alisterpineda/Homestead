using Homestead.WebApi.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.PatchDocumentVersion;

public class PatchDocumentVersionModel
{
    public string? Label { get; set; }
    public string? ExtractedText { get; set; }
}

public record PatchDocumentVersionCommand : IRequest<PatchDocumentVersionResponse>, IRequireDocumentAuthorization
{
    public Guid DocumentId { get; init; }
    public Guid Id { get; init; }
    public required JsonPatchDocument<PatchDocumentVersionModel> PatchDocument { get; init; }

    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Write;
}
