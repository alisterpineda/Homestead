using MediatR;
using Homestead.WebApi.Common;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Documents.Features.Settings.PatchDocumentSettings;

public record PatchDocumentSettingsCommand : ICommand<DocumentSettingsResponse>
{
    public required JsonPatchDocument<DocumentSettings> PatchDocument { get; init; }
}
