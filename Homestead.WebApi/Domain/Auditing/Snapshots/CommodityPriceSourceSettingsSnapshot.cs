using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record CommodityPriceSourceSettingsSnapshot
    : IAuditSnapshot<CommodityPriceSourceSettingsSnapshot, CommodityPriceSourceSettings>
{
    public required bool IsEnabled { get; init; }
    public required string? Config { get; init; }
    public required string? PairAllowList { get; init; }
    public required string? LastImportStatus { get; init; }

    public static CommodityPriceSourceSettingsSnapshot From(CommodityPriceSourceSettings source) => new()
    {
        IsEnabled = source.IsEnabled,
        Config = source.Config,
        PairAllowList = source.PairAllowList,
        LastImportStatus = source.LastImportStatus,
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
        if (LastImportStatus != other.LastImportStatus)
            changes.Add(new ScalarFieldChange("LastImportStatus", LastImportStatus, other.LastImportStatus));

        return changes;
    }
}
