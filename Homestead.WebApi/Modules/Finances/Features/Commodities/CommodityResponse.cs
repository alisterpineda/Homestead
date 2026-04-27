using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities;

public record CommodityResponse
{
    public required Guid Id { get; init; }
    public required string Symbol { get; init; }
    public required string? Exchange { get; init; }
    public required string Name { get; init; }
    public required CommodityKind Kind { get; init; }
    public required short DisplayPrecision { get; init; }
    public required bool IsSystemManaged { get; init; }
    public required int PriceCount { get; init; }
    public required DateTimeOffset? LatestPriceAt { get; init; }
    public required bool CanDelete { get; init; }
}
