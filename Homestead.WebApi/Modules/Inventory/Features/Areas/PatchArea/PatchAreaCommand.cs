using Homestead.WebApi.Common;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.PatchArea;

public class PatchAreaModel
{
    public string Name { get; set; } = string.Empty;
}

public record PatchAreaCommand : ICommand<AreaResponse>
{
    public Guid Id { get; init; }
    public required JsonPatchDocument<PatchAreaModel> PatchDocument { get; init; }
}
