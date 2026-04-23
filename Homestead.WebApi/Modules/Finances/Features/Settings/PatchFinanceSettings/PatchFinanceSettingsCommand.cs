using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Finances.Features.Settings.PatchFinanceSettings;

public record PatchFinanceSettingsCommand : IRequest<FinanceSettingsResponse>
{
    public required JsonPatchDocument<FinanceSettings> PatchDocument { get; init; }
}
