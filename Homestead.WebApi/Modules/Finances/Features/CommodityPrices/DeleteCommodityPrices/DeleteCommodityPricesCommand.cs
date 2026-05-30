using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.DeleteCommodityPrices;

public record DeleteCommodityPricesCommand(IReadOnlyList<Guid> Ids)
    : ICommand<DeleteCommodityPricesResponse>;
