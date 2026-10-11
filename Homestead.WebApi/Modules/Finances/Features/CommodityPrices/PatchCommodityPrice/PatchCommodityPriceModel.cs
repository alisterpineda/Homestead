namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.PatchCommodityPrice;

public class PatchCommodityPriceModel
{
    public DateTimeOffset PriceAt { get; set; }
    public decimal Rate { get; set; }
}
