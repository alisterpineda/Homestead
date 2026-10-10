namespace Homestead.WebApi.Modules.Inventory.Features.Tags.CreateTag;

public record CreateInventoryTagCommand(string Path, string? Color = null, string? Description = null);
