using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Finances.Features.Settings.PatchFinanceSettings;

public record PatchFinanceSettingsCommand
{
    public required JsonPatchDocument<FinanceSettings> PatchDocument { get; init; }
}
