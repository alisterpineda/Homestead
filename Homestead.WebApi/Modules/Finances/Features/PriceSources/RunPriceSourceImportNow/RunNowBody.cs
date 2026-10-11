namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.RunPriceSourceImportNow;

public record RunNowBody
{
    public DateTimeOffset? FromAt { get; init; }
    public DateTimeOffset? ToAt { get; init; }
    public IReadOnlyList<Guid>? CommodityIds { get; init; }
}
