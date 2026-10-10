using System.Text.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.GetPriceSource;

public class GetPriceSourceHandler(
    AppDbContext db,
    IPriceSourceRegistry sources,
    IPriceSourceScheduleStore scheduleStore)
{
    public async Task<GetPriceSourceResponse> Handle(GetPriceSourceQuery request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Source))
            throw new EntityNotFoundException("Price source not found.");

        var settings = await db.CommodityPriceSourceSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Source == request.Source, ct);

        var priceCount = await db.CommodityPrices
            .AsNoTracking()
            .CountAsync(p => p.Source == request.Source, ct);

        var provider = sources.Get(request.Source);
        var kind = request.Source.Kind();

        PriceSourceScheduleSummary? scheduleSummary = null;
        if (kind == CommodityPriceSourceKind.External)
        {
            var schedule = await scheduleStore.FindBySourceAsync(request.Source, ct);
            if (schedule is not null)
            {
                scheduleSummary = new PriceSourceScheduleSummary
                {
                    Id = schedule.Id,
                    CronExpression = schedule.CronExpression,
                    IsEnabled = schedule.IsEnabled,
                    Priority = schedule.Priority,
                    NextRunAt = schedule.NextRunAt,
                    LastRunAt = schedule.LastRunAt,
                };
            }
        }

        IReadOnlyList<string>? pairAllowList = null;
        if (!string.IsNullOrWhiteSpace(settings?.PairAllowList))
        {
            try
            {
                pairAllowList = JsonSerializer.Deserialize<string[]>(settings.PairAllowList);
            }
            catch (JsonException) { /* defensive: stored value may be malformed */ }
        }

        return new GetPriceSourceResponse
        {
            Source = request.Source,
            DisplayName = request.Source.DisplayName(),
            Kind = kind,
            IsEnabled = sources.IsEnabled(request.Source),
            IsConfigured = sources.IsConfigured(request.Source),
            ConfigurationError = provider?.ConfigurationError,
            HasApiKey = provider?.HasApiKey,
            BaseUrl = provider?.BaseUrl,
            PairAllowList = pairAllowList,
            LastImportAt = settings?.LastImportAt,
            LastImportStatus = settings?.LastImportStatus,
            LastImportMessage = settings?.LastImportMessage,
            SupportedPairs = provider?.SupportedPairs ?? Array.Empty<PriceProviderSupportedPair>(),
            CoveredSecurityExchanges = provider?.CoveredSecurityExchanges ?? Array.Empty<string>(),
            CoveredQuoteCurrencies = provider?.CoveredQuoteCurrencies ?? Array.Empty<string>(),
            Schedule = scheduleSummary,
            PriceCount = priceCount,
        };
    }
}
