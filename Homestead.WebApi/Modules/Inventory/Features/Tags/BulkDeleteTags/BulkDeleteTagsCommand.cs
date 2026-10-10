namespace Homestead.WebApi.Modules.Inventory.Features.Tags.BulkDeleteTags;

public record BulkDeleteInventoryTagsCommand(List<Guid> Ids);
