using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public static class CommodityPriceSourceQueryExtensions
{
    public static async Task<IReadOnlySet<CommodityPriceSource>> GetEnabledSourcesAsync(
        AppDbContext db, CancellationToken ct)
    {
        var settingsBySource = await db.CommodityPriceSourceSettings
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Source, ct);
        return ComputeEnabledSources(settingsBySource);
    }

    public static IReadOnlySet<CommodityPriceSource> ComputeEnabledSources(
        IReadOnlyDictionary<CommodityPriceSource, CommodityPriceSourceSettings> settingsBySource)
    {
        var enabled = new HashSet<CommodityPriceSource>();
        foreach (var source in Enum.GetValues<CommodityPriceSource>())
        {
            settingsBySource.TryGetValue(source, out var settings);
            if (IsEnabledSource(source, settings)) enabled.Add(source);
        }
        return enabled;
    }

    /// <summary>
    /// The single definition of "enabled" for a price source. External sources need a settings
    /// row with IsEnabled=true; Manual and Transaction are always-on by convention.
    /// </summary>
    public static bool IsEnabledSource(CommodityPriceSource source, CommodityPriceSourceSettings? settings) =>
        source.Kind() != CommodityPriceSourceKind.External || (settings?.IsEnabled ?? false);
}
