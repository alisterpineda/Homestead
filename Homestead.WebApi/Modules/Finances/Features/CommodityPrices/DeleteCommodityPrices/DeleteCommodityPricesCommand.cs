namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.DeleteCommodityPrices;

public record DeleteCommodityPricesCommand(IReadOnlyList<Guid> Ids);
