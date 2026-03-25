using Homestead.WebApi.Features.Admin.Settings;
using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Features.Admin.Settings.PatchDocumentSettings;

public record PatchDocumentSettingsCommand : IRequest<DocumentSettingsResponse>
{
    public required JsonPatchDocument<DocumentSettings> PatchDocument { get; init; }
}
