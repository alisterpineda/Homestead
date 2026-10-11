namespace Homestead.WebApi.Modules.Inventory.Features.Tags.PatchTag;

public class PatchInventoryTagModel
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public string? Color { get; set; }
    public string? Description { get; set; }
}
