using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSources;

public record GetPriceSourcesQuery : IRequest<GetPriceSourcesResponse>;
