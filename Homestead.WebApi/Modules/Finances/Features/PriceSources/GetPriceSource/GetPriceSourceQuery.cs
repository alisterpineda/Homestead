using Homestead.WebApi.Domain.Finances;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSource;

public record GetPriceSourceQuery(CommodityPriceSource Source) : IRequest<GetPriceSourceResponse>;
