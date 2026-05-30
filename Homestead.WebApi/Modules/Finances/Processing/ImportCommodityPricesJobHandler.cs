using System.Text.Json;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.Processing.Commands;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Modules.Finances.Processing;

public class ImportCommodityPricesJobHandler(
    AppDbContext db,
    IServiceProvider serviceProvider,
    ISender sender,
    IServiceScopeFactory scopeFactory,
    ILogger<ImportCommodityPricesJobHandler> logger) : IJobHandler
{
    private const int MaxErrorLength = 4096;

    public ProcessingJobType Type => ProcessingJobType.ImportCommodityPrices;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<ImportCommodityPricesPayload>()
            ?? throw new InvalidOperationException("ImportCommodityPrices payload is missing or invalid.");

        if (payload.Source.Kind() != CommodityPriceSourceKind.External)
            throw new PriceImportException(
                "source_system_managed",
                $"Price source '{payload.Source}' is system-managed and cannot be imported.");

        try
        {
            await RunImportAsync(job, payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordFailureAsync(payload.Source, ex, ct);
            throw;
        }
    }

    private async Task RunImportAsync(
        ProcessingJob job,
        ImportCommodityPricesPayload payload,
        CancellationToken ct)
    {
        var settings = await db.CommodityPriceSourceSettings
            .FirstOrDefaultAsync(s => s.Source == payload.Source, ct);

        if (settings is null || !settings.IsEnabled)
            throw new PriceImportException(
                "source_disabled",
                $"Price source '{payload.Source}' is not enabled.");

        var provider = serviceProvider.GetKeyedService<IPriceProvider>(payload.Source);
        if (provider is null)
            throw new PriceImportException(
                "provider_not_registered",
                $"No provider is registered for price source '{payload.Source}'.");

        var commodities = await ResolveCommoditiesAsync(provider, settings, payload, ct);

        var now = DateTimeOffset.UtcNow;
        var (fromAt, anchorSource) = payload.FromAt is { } explicitFrom
            ? (explicitFrom, PriceImportAnchorSource.ExplicitOverride)
            : await ResolveImportAnchorAsync(payload.Source, provider, commodities, now, ct);
        var toAt = payload.ToAt ?? now;

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(
                "Price import {Source}: {CommodityCount} commodities, anchor={AnchorSource} window={FromAt:o}..{ToAt:o}",
                payload.Source, commodities.Count, anchorSource, fromAt, toAt);

        var result = await provider.ImportAsync(
            new PriceImportRequest(commodities, fromAt, toAt, anchorSource, settings.Config), ct);

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(
                "Price import {Source}: provider returned {ObservationCount} observations, warning={Warning}",
                payload.Source, result.Observations.Count, result.Warning ?? "(none)");

        // Fold the DB write + audit into the unit-of-work chokepoint. Runs in this job's DI scope, so the
        // command handler shares the pooled AppDbContext (and the settings entity loaded above), and the
        // UnitOfWorkBehavior commits once after the command. Scheduled imports stay system-attributed;
        // manual runs are attributed to the initiating user.
        await sender.Send(new ApplyCommodityPriceObservationsCommand
        {
            Source = payload.Source,
            Observations = result.Observations,
            Warning = result.Warning,
            ActorUserId = job.ScheduleId is null ? job.CreatedByUserId : null,
        }, ct);
    }

    private async Task<IReadOnlyCollection<Commodity>> ResolveCommoditiesAsync(
        IPriceProvider provider,
        CommodityPriceSourceSettings settings,
        ImportCommodityPricesPayload payload,
        CancellationToken ct)
    {
        var heldFromSplits = db.AccountTransactionSplits
            .Where(s => !s.Account.IsArchived)
            .Select(s => s.CommodityId);

        var heldFromUnitPrice = db.AccountTransactionSplits
            .Where(s => !s.Account.IsArchived && s.UnitPriceCommodityId != null)
            .Select(s => s.UnitPriceCommodityId!.Value);

        var reportingCurrencies = db.Accounts
            .Where(a => !a.IsArchived)
            .Select(a => a.ReportingCurrencyId);

        var heldQuery = heldFromSplits.Concat(heldFromUnitPrice).Concat(reportingCurrencies).Distinct();
        var allowSymbols = ParseAllowList(settings.PairAllowList);

        var heldCommodities = await db.Commodities
            .Where(c => heldQuery.Contains(c.Id) || allowSymbols.Contains(c.Symbol))
            .ToListAsync(ct);

        var supportedSymbols = provider.SupportedPairs
            .Select(p => (p.CommoditySymbol, p.Exchange))
            .ToHashSet();
        var quoteSymbols = provider.SupportedPairs.Select(p => p.QuoteCurrencySymbol)
            .Concat(provider.CoveredQuoteCurrencies)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var coveredExchanges = provider.CoveredSecurityExchanges
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filtered = heldCommodities
            .Where(c => supportedSymbols.Contains((c.Symbol, c.Exchange))
                || quoteSymbols.Contains(c.Symbol)
                || (c.Kind == CommodityKind.Security
                    && c.Exchange is not null
                    && coveredExchanges.Contains(c.Exchange)));

        if (payload.CommodityIds is { Count: > 0 })
        {
            var idFilter = payload.CommodityIds.ToHashSet();
            filtered = filtered.Where(c => idFilter.Contains(c.Id));
        }

        return filtered.ToList();
    }

    private async Task<(DateTimeOffset FromAt, PriceImportAnchorSource AnchorSource)> ResolveImportAnchorAsync(
        CommodityPriceSource source,
        IPriceProvider provider,
        IReadOnlyCollection<Commodity> activeCommodities,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var activeIds = activeCommodities.Select(c => c.Id).ToList();

        // min(max(PriceAt)) across currently-held (commodity, quote) pairs for this source.
        // Filtering to activeIds prevents frozen pairs (delisted, archived) from dragging the anchor.
        // The Cast<DateTimeOffset?> idiom lets us distinguish "no rows" from default(DateTimeOffset).
        var earliestWatermark = await db.CommodityPrices
            .Where(p => p.Source == source && activeIds.Contains(p.CommodityId))
            .GroupBy(p => new { p.CommodityId, p.QuoteCurrencyId })
            .Select(g => (DateTimeOffset?)g.Max(p => p.PriceAt))
            .MinAsync(ct);

        if (earliestWatermark is null)
        {
            var earliest = await ResolveEarliestActivityAsync(now, ct);
            return (earliest, PriceImportAnchorSource.EarliestActivity);
        }

        return (earliestWatermark.Value - provider.RevisionLookback, PriceImportAnchorSource.Watermark);
    }

    private async Task<DateTimeOffset> ResolveEarliestActivityAsync(DateTimeOffset now, CancellationToken ct)
    {
        var earliest = await db.AccountTransactions
            .Where(t => t.Splits.Any(s => !s.Account.IsArchived))
            .OrderBy(t => t.OccurredAt)
            .Select(t => (DateTimeOffset?)t.OccurredAt)
            .FirstOrDefaultAsync(ct);

        return earliest ?? now.AddDays(-30);
    }

    private async Task RecordFailureAsync(
        CommodityPriceSource source,
        Exception ex,
        CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var freshDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await freshDb.CommodityPriceSourceSettings
                .FirstOrDefaultAsync(s => s.Source == source, ct);
            if (settings is null) return;

            settings.LastImportStatus = ImportStatus.Failed;
            settings.LastImportMessage = Truncate(ex.Message);
            settings.UpdatedAt = DateTimeOffset.UtcNow;
            await freshDb.SaveChangesAsync(ct);
        }
        catch (Exception inner)
        {
            logger.LogError(inner, "Failed to record import failure for source {Source}", source);
        }
    }

    private static string? Truncate(string? message) =>
        message is { Length: > MaxErrorLength } ? message[..MaxErrorLength] : message;

    private static HashSet<string> ParseAllowList(string? rawList)
    {
        if (string.IsNullOrWhiteSpace(rawList))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(rawList);
            if (parsed is null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return parsed
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
