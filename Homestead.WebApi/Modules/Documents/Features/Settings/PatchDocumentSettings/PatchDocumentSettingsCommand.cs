using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Documents.Features.Settings.PatchDocumentSettings;

public record PatchDocumentSettingsCommand
{
    public required JsonPatchDocument<DocumentSettings> PatchDocument { get; init; }
}
