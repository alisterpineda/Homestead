using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSources;

public class GetPriceSourcesHandler(AppDbContext db)
    : IRequestHandler<GetPriceSourcesQuery, GetPriceSourcesResponse>
{
    public async Task<GetPriceSourcesResponse> Handle(GetPriceSourcesQuery request, CancellationToken ct)
    {
        var sources = Enum.GetValues<CommodityPriceSource>();

        var settingsBySource = await db.CommodityPriceSourceSettings
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Source, ct);

        var counts = await db.CommodityPrices
            .AsNoTracking()
            .GroupBy(p => p.Source)
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Source, x => x.Count, ct);

        var items = new List<PriceSourceSummary>(sources.Length);
        foreach (var source in sources)
        {
            settingsBySource.TryGetValue(source, out var settings);
            counts.TryGetValue(source, out var count);

            var kind = source.Kind();
            var isEnabled = kind == CommodityPriceSourceKind.External
                ? settings?.IsEnabled ?? false
                : true;

            items.Add(new PriceSourceSummary
            {
                Source = source,
                DisplayName = source.DisplayName(),
                Kind = kind,
                IsEnabled = isEnabled,
                PriceCount = count,
                LastImportAt = settings?.LastImportAt,
                LastImportStatus = settings?.LastImportStatus,
            });
        }

        return new GetPriceSourcesResponse { Items = items };
    }
}
