namespace Homestead.WebApi.Modules.Inventory.Features.Tags.GetTagsByIds;

public record GetInventoryTagsByIdsQuery(IReadOnlyList<Guid> Ids);
