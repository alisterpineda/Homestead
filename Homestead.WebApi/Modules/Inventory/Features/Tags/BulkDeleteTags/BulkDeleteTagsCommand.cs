using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.BulkDeleteTags;

public record BulkDeleteInventoryTagsCommand(List<Guid> Ids) : ICommand<BulkDeleteInventoryTagsResponse>;
