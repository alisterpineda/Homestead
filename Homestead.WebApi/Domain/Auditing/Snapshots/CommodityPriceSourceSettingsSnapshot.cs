using System.Security.Cryptography;
using System.Text;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record CommodityPriceSourceSettingsSnapshot
    : IAuditSnapshot<CommodityPriceSourceSettingsSnapshot, CommodityPriceSourceSettings>
{
    public required bool IsEnabled { get; init; }
    public required string? Config { get; init; }
    public required string? PairAllowList { get; init; }

    public static CommodityPriceSourceSettingsSnapshot From(CommodityPriceSourceSettings source) =>
        From(source, Array.Empty<string>());

    public static CommodityPriceSourceSettingsSnapshot From(
        CommodityPriceSourceSettings source,
        IEnumerable<string> secretKeys) => new()
    {
        IsEnabled = source.IsEnabled,
        Config = JsonSecretConfig.Mask(source.Config, secretKeys),
        PairAllowList = source.PairAllowList,
    };

    public List<FieldChange> Diff(CommodityPriceSourceSettingsSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (IsEnabled != other.IsEnabled)
            changes.Add(new ScalarFieldChange("IsEnabled", IsEnabled.ToString(), other.IsEnabled.ToString()));
        if (Config != other.Config)
            changes.Add(new ScalarFieldChange("Config", Config, other.Config));
        if (PairAllowList != other.PairAllowList)
            changes.Add(new ScalarFieldChange("PairAllowList", PairAllowList, other.PairAllowList));

        return changes;
    }

    public static Guid SourceToGuid(CommodityPriceSource source) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes($"CommodityPriceSourceSettings:{source}")));
}
