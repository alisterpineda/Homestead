using Homestead.WebApi.Common;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Finances.Features.Settings.PatchFinanceSettings;

public record PatchFinanceSettingsCommand : ICommand<FinanceSettingsResponse>
{
    public required JsonPatchDocument<FinanceSettings> PatchDocument { get; init; }
}
