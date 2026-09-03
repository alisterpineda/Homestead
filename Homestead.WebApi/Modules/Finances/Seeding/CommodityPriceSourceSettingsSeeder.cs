using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Seeding;

public static class CommodityPriceSourceSettingsSeeder
{
    private static IReadOnlyList<CommodityPriceSource> ExternalSources { get; } =
        [.. Enum.GetValues<CommodityPriceSource>().Where(s => s.Kind() == CommodityPriceSourceKind.External)];

    // `_` parameter is required by EF's UseSeeding delegate shape (carries whether
    // the DB was just created); we don't branch on it.
    public static void Seed(DbContext context, bool _ = false)
    {
        var set = context.Set<CommodityPriceSourceSettings>();
        var existing = set.Select(s => s.Source).ToHashSet();
        var toAdd = BuildMissing(existing);
        if (toAdd.Count == 0) return;

        set.AddRange(toAdd);
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, bool _ = false, CancellationToken cancellationToken = default)
    {
        var set = context.Set<CommodityPriceSourceSettings>();
        var existing = await set.Select(s => s.Source).ToHashSetAsync(cancellationToken);
        var toAdd = BuildMissing(existing);
        if (toAdd.Count == 0) return;

        await set.AddRangeAsync(toAdd, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static List<CommodityPriceSourceSettings> BuildMissing(HashSet<CommodityPriceSource> existing)
    {
        var toAdd = new List<CommodityPriceSourceSettings>();
        foreach (var source in ExternalSources)
        {
            if (existing.Contains(source)) continue;
            toAdd.Add(new CommodityPriceSourceSettings { Source = source });
        }
        return toAdd;
    }
}
