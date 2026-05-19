using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Finances;

public class CommodityPriceSourceSettings
{
    public CommodityPriceSource Source { get; set; }

    public bool IsEnabled { get; set; }

    public string? Config { get; set; }

    public string? PairAllowList { get; set; }

    /// <summary>
    /// Wall-clock timestamp of the last completed import. Audit / UI use only —
    /// do NOT use this as a data window anchor. The next-run window is derived
    /// from <c>CommodityPrices</c> in <c>ImportCommodityPricesJobHandler.ResolveImportAnchorAsync</c>.
    /// </summary>
    public DateTimeOffset? LastImportAt { get; set; }

    public string? LastImportStatus { get; set; }

    public string? LastImportMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
