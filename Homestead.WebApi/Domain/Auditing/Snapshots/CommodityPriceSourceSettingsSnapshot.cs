using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record CommodityPriceSourceSettingsSnapshot
    : IAuditSnapshot<CommodityPriceSourceSettingsSnapshot, CommodityPriceSourceSettings>
{
    public required string? PairAllowList { get; init; }

    public static CommodityPriceSourceSettingsSnapshot From(CommodityPriceSourceSettings source) => new()
    {
        PairAllowList = source.PairAllowList,
    };

    public List<FieldChange> Diff(CommodityPriceSourceSettingsSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (PairAllowList != other.PairAllowList)
            changes.Add(new ScalarFieldChange("PairAllowList", PairAllowList, other.PairAllowList));

        return changes;
    }

    public static Guid SourceToGuid(CommodityPriceSource source) =>
        DeterministicGuid.FromKey($"CommodityPriceSourceSettings:{source}");
}
