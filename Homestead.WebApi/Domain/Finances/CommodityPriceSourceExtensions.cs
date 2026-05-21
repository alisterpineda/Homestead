using Homestead.WebApi.Common;

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
        CommodityPriceSource.AlphaVantage => CommodityPriceSourceKind.External,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, $"Unsupported price source: {source}"),
    };

    /// <summary>
    /// Same-PriceAt tiebreaker rank: External (0) → Transaction (1) → Manual (2).
    /// Higher rank wins ties — Manual is the durable override per commodities.md §"Same-PriceAt tiebreaker".
    /// </summary>
    public static int TiebreakerRank(this CommodityPriceSource source) => source.Kind() switch
    {
        CommodityPriceSourceKind.External => 0,
        CommodityPriceSourceKind.Transaction => 1,
        CommodityPriceSourceKind.Manual => 2,
        _ => 0,
    };

    public static string DisplayName(this CommodityPriceSource source) => source switch
    {
        CommodityPriceSource.Transaction => "Transaction",
        CommodityPriceSource.Manual => "Manual",
        CommodityPriceSource.BankOfCanada => "Bank of Canada",
        CommodityPriceSource.AlphaVantage => "Alpha Vantage",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, $"Unsupported price source: {source}"),
    };

    /// <summary>
    /// Throws <see cref="EntityNotFoundException"/> if the enum value is undefined,
    /// or <see cref="BusinessRuleException"/> if the source is system-managed (Transaction/Manual).
    /// </summary>
    public static void RequireExternal(this CommodityPriceSource source, string operation)
    {
        if (!Enum.IsDefined(source))
            throw new EntityNotFoundException("Price source not found.");
        if (source.Kind() != CommodityPriceSourceKind.External)
            throw new BusinessRuleException(
                $"Price source '{source}' is system-managed and cannot be {operation}.");
    }

    public static void RequireManual(this CommodityPriceSource source, string operation)
    {
        if (source != CommodityPriceSource.Manual)
            throw new BusinessRuleException(
                $"Only manually entered prices can be {operation}.");
    }

    public static void RequireDeletable(this CommodityPriceSource source)
    {
        if (source.Kind() == CommodityPriceSourceKind.Transaction)
            throw new BusinessRuleException(
                "Transaction-linked prices cannot be deleted directly. " +
                "Edit or delete the source transaction instead.");
    }
}
