using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.PatchBrand;

public class PatchBrandModel
{
    public string Name { get; set; } = string.Empty;
}

public record PatchBrandCommand
{
    public Guid Id { get; init; }
    public required JsonPatchDocument<PatchBrandModel> PatchDocument { get; init; }
}
