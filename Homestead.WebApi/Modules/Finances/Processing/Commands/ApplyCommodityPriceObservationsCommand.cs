using System.Globalization;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Processing.Commands;

/// <summary>
/// Internal command (no HTTP surface, no authorization marker — authorization happened at import enqueue)
/// that upserts provider price observations and stamps the source's last-import status in one unit of
/// work. Mirrors the Documents job-fold: <see cref="ImportCommodityPricesJobHandler"/> keeps the provider
/// HTTP IO and hands the in-memory observations here so the DB write + audit run through the single
/// persistence/audit chokepoint. No explicit save — the job handler's unit of work commits.
/// </summary>
public record ApplyCommodityPriceObservationsCommand
{
    public required CommodityPriceSource Source { get; init; }
    public required IReadOnlyList<PriceObservation> Observations { get; init; }

    /// <summary>Provider partial-success warning; null on full success. Clamped to the column bound when stamped.</summary>
    public string? Warning { get; init; }
}

public interface IApplyCommodityPriceObservationsHandler
{
    Task Handle(ApplyCommodityPriceObservationsCommand request, CancellationToken ct);
}

public class ApplyCommodityPriceObservationsHandler(AppDbContext db, IAuditContext audit)
    : IApplyCommodityPriceObservationsHandler
{
    // Bound for LastImportMessage — enforced here so the persistence seam owns the cap rather than
    // trusting every caller to pre-truncate.
    private const int MaxErrorLength = 4096;

    public async Task Handle(ApplyCommodityPriceObservationsCommand request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Last-wins dedup: a single provider batch may carry multiple observations for the same
        // (commodity, quote, instant). Collapse to the last so we never emit two inserts for one key,
        // which would violate the unique index on CommodityPrice and fail the whole import.
        var observations = request.Observations
            .GroupBy(o => (o.CommodityId, o.QuoteCurrencyId, o.PriceAt))
            .Select(g => g.Last())
            .ToList();

        if (observations.Count > 0)
        {
            var commodityIds = observations.Select(o => o.CommodityId).Distinct().ToList();
            var quoteIds = observations.Select(o => o.QuoteCurrencyId).Distinct().ToList();
            var instants = observations.Select(o => o.PriceAt).Distinct().ToList();

            var existing = await db.CommodityPrices
                .Where(p => p.Source == request.Source
                    && commodityIds.Contains(p.CommodityId)
                    && quoteIds.Contains(p.QuoteCurrencyId)
                    && instants.Contains(p.PriceAt))
                .ToListAsync(ct);

            var existingByKey = existing.ToDictionary(p => (p.CommodityId, p.QuoteCurrencyId, p.PriceAt));

            foreach (var observation in observations)
            {
                var key = (observation.CommodityId, observation.QuoteCurrencyId, observation.PriceAt);
                if (existingByKey.TryGetValue(key, out var match))
                {
                    if (match.Rate == observation.Rate) continue;
                    var oldRate = match.Rate;
                    match.Rate = observation.Rate;
                    match.UpdatedAt = now;
                    audit.TrackScalar("CommodityPrice", match.Id, "Rate",
                        oldRate.ToString(CultureInfo.InvariantCulture),
                        observation.Rate.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    var price = new CommodityPrice
                    {
                        CommodityId = observation.CommodityId,
                        QuoteCurrencyId = observation.QuoteCurrencyId,
                        PriceAt = observation.PriceAt,
                        Rate = observation.Rate,
                        Source = request.Source,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    db.CommodityPrices.Add(price);
                    audit.Created("CommodityPrice", price.Id);
                }
            }
        }

        // Stamp the source's last-import status. The settings row is guaranteed to exist by the import
        // job's pre-checks, so its absence is an invariant violation rather than something to silently
        // skip. FindAsync hits the change-tracker identity map (the job loaded this row in the same scope),
        // so this is a no-op cache read, not a second round-trip.
        var settings = await db.CommodityPriceSourceSettings.FindAsync(new object?[] { request.Source }, ct)
            ?? throw new InvalidOperationException(
                $"Price source settings for '{request.Source}' were not found.");

        settings.LastImportAt = now;
        settings.LastImportStatus = request.Warning is null ? ImportStatus.Success : ImportStatus.PartialSuccess;
        settings.LastImportMessage = Truncate(request.Warning);
        settings.UpdatedAt = now;

        // No explicit save — the job handler's unit of work commits (and flushes audit, attributed to the
        // actor it set) after this handler returns.
    }

    private static string? Truncate(string? message) =>
        message is { Length: > MaxErrorLength } ? message[..MaxErrorLength] : message;
}
