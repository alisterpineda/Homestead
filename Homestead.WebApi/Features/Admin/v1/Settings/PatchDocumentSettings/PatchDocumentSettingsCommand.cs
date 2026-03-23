using Homestead.WebApi.Configuration;
using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Features.Admin.v1.Settings.PatchDocumentSettings;

public record PatchDocumentSettingsCommand : IRequest<DocumentSettingsResponse>
{
    public required JsonPatchDocument<DocumentSettings> PatchDocument { get; init; }
}
