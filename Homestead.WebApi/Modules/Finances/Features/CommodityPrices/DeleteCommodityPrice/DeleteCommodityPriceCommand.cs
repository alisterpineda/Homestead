using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.DeleteCommodityPrice;

public record DeleteCommodityPriceCommand(Guid Id) : IRequest<bool>;
