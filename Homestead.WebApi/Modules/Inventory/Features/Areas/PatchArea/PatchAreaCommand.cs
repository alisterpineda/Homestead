using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.PatchArea;

public class PatchAreaModel
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Parent area; null promotes the area to a top-level root.</summary>
    public Guid? ParentAreaId { get; set; }
}

public record PatchAreaCommand
{
    public Guid Id { get; init; }
    public required JsonPatchDocument<PatchAreaModel> PatchDocument { get; init; }
}
