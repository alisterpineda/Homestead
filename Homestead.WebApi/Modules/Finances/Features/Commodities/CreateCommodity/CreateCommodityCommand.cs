using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.CreateCommodity;

public record CreateCommodityCommand : IRequest<CommodityResponse>
{
    public required string Symbol { get; init; }
    public required string Exchange { get; init; }
    public required string Name { get; init; }
    public short DisplayPrecision { get; init; } = 4;
}
