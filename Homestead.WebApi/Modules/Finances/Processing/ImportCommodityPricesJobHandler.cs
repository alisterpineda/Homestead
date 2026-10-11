using System.Text.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.Processing.Commands;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Processing;

public class ImportCommodityPricesJobHandler(
    AppDbContext db,
    IPriceSourceRegistry sources,
    IUnitOfWork unitOfWork,
    IApplyCommodityPriceObservationsHandler applyObservations,
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

        await RunImportAsync(job, payload, ct);
    }

    /// <summary>
    /// Provider calls are HTTP against third parties, so an outage measured in minutes — longer than the
    /// HttpClient resilience handler rides out — is worth waiting through. Safe to re-run: observations are
    /// upserted by (commodity, quote, instant) and a rate that did not change writes no audit.
    /// Import blockers (<see cref="PriceImportException"/>) are structured and so never retried.
    /// </summary>
    public int MaxAttempts => 3;

    /// <summary>
    /// Stamps the failure onto the source's settings row, once, after the last attempt — not on every
    /// attempt, which would show the source as Failed while a retry was still queued.
    /// </summary>
    /// <remarks>
    /// Runs in a fresh scope, so the injected <c>db</c> is not the one the failed run left behind. A
    /// system-managed source never reaches the import and has nothing to stamp.
    /// </remarks>
    public async Task OnFinallyFailedAsync(ProcessingJob job, Exception exception, CancellationToken ct)
    {
        ImportCommodityPricesPayload? payload;
        try
        {
            payload = job.GetPayload<ImportCommodityPricesPayload>();
        }
        catch (JsonException)
        {
            return;
        }

        if (payload is null || payload.Source.Kind() != CommodityPriceSourceKind.External)
            return;

        await RecordFailureAsync(payload.Source, exception, ct);
    }

    private async Task RunImportAsync(
        ProcessingJob job,
        ImportCommodityPricesPayload payload,
        CancellationToken ct)
    {
        var provider = sources.Get(payload.Source);
        if (PriceSourceConfiguration.ImportBlocker(provider, payload.Source) is { } blocked)
            throw new PriceImportException(blocked.Code, blocked.Message);

        // The settings row only carries product data now (the watch-list), so it no longer gates the
        // import: here its absence just means an empty allow-list. The seeder guarantees one per external
        // source, and ApplyCommodityPriceObservationsCommand still requires it to stamp LastImport*.
        var settings = await db.CommodityPriceSourceSettings
            .FirstOrDefaultAsync(s => s.Source == payload.Source, ct);

        var commodities = await ResolveCommoditiesAsync(provider!, settings?.PairAllowList, payload, ct);

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
            new PriceImportRequest(commodities, fromAt, toAt, anchorSource), ct);

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(
                "Price import {Source}: provider returned {ObservationCount} observations, warning={Warning}",
                payload.Source, result.Observations.Count, result.Warning ?? "(none)");

        // Fold the DB write + audit into the unit-of-work chokepoint. Runs in this job's DI scope, so the
        // command handler shares the same scoped AppDbContext (and the settings entity loaded above), and
        // the transaction commits once after the command. Scheduled imports stay system-attributed;
        // manual runs are attributed to the initiating user.
        var command = new ApplyCommodityPriceObservationsCommand
        {
            Source = payload.Source,
            Observations = result.Observations,
            Warning = result.Warning,
        };
        unitOfWork.SetActor(job.ScheduleId is null ? job.CreatedByUserId : null);
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await applyObservations.HandleAsync(command, ct);
            return true;
        }, ct);
    }

    private async Task<IReadOnlyCollection<Commodity>> ResolveCommoditiesAsync(
        IPriceProvider provider,
        string? pairAllowList,
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
        var allowSymbols = ParseAllowList(pairAllowList);

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
            var settings = await db.CommodityPriceSourceSettings
                .FirstOrDefaultAsync(s => s.Source == source, ct);
            if (settings is null) return;

            settings.LastImportStatus = ImportStatus.Failed;
            settings.LastImportMessage = Truncate(ex.Message);
            settings.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
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
