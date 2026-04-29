namespace Homestead.WebApi.Domain.Finances;

public enum CommodityPriceSourceKind
{
    Transaction,
    Manual,
    External,
}

public static class CommodityPriceSourceExtensions
{
    public static CommodityPriceSourceKind Kind(this CommodityPriceSource source) => source switch
    {
        CommodityPriceSource.Transaction => CommodityPriceSourceKind.Transaction,
        CommodityPriceSource.Manual => CommodityPriceSourceKind.Manual,
        CommodityPriceSource.BankOfCanada => CommodityPriceSourceKind.External,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, $"Unsupported price source: {source}"),
    };
}
