using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Features.Documents.Settings.PatchDocumentSettings;

public record PatchDocumentSettingsCommand : IRequest<DocumentSettingsResponse>
{
    public required JsonPatchDocument<DocumentSettings> PatchDocument { get; init; }
}
