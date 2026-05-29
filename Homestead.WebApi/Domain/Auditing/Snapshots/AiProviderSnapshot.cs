using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record AiProviderSnapshot : IAuditSnapshot<AiProviderSnapshot, AiProvider>
{
    public required string Name { get; init; }
    public required string ProviderType { get; init; }
    public string? Configuration { get; init; }
    public required bool IsEnabled { get; init; }

    // Records the RAW configuration (including secrets) on purpose — audit must capture the true
    // before/after values. This intentionally diverges from CommodityPriceSourceSettingsSnapshot,
    // which masks secrets in its snapshot.
    public static AiProviderSnapshot From(AiProvider source) => new()
    {
        Name = source.Name,
        ProviderType = source.ProviderType.ToString(),
        Configuration = source.Configuration,
        IsEnabled = source.IsEnabled
    };

    public List<FieldChange> Diff(AiProviderSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Name != other.Name)
            changes.Add(new ScalarFieldChange("Name", Name, other.Name));
        if (ProviderType != other.ProviderType)
            changes.Add(new ScalarFieldChange("ProviderType", ProviderType, other.ProviderType));
        if (Configuration != other.Configuration)
            changes.Add(new ScalarFieldChange("Configuration", Configuration, other.Configuration));
        if (IsEnabled != other.IsEnabled)
            changes.Add(new ScalarFieldChange("IsEnabled", IsEnabled.ToString(), other.IsEnabled.ToString()));

        return changes;
    }
}
