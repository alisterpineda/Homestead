using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public class LinkedPriceManager(AppDbContext db, IAuditContext audit) : ILinkedPriceManager
{
    public async Task SyncForTransactionAsync(AccountTransaction transaction, CancellationToken ct)
    {
        var annotatedSplits = transaction.Splits
            .Where(s => s.UnitPriceRate is not null && s.UnitPriceCommodityId is not null)
            .ToList();

        var existingRows = await db.CommodityPrices
            .Where(p => p.CreatedByTransactionId == transaction.Id)
            .ToListAsync(ct);

        // Existing rows pair to splits by (commodity, quote): two annotated splits sharing one
        // pair would resolve to the same price row, which the price uniqueness index forbids.
        ValidateUniqueTargets(annotatedSplits);

        var matched = new HashSet<Guid>();

        foreach (var split in annotatedSplits)
        {
            var quoteId = split.UnitPriceCommodityId!.Value;
            var rate = split.UnitPriceRate!.Value;

            var existing = existingRows.FirstOrDefault(p =>
                p.CommodityId == split.CommodityId && p.QuoteCurrencyId == quoteId);

            if (existing is null)
            {
                await EnsureNoTransactionCollisionAsync(split.CommodityId, quoteId, transaction.OccurredAt, excludeId: null, ct);

                var created = new CommodityPrice
                {
                    CommodityId = split.CommodityId,
                    QuoteCurrencyId = quoteId,
                    PriceAt = transaction.OccurredAt,
                    Rate = rate,
                    Source = CommodityPriceSource.Transaction,
                    CreatedByTransactionId = transaction.Id,
                };
                db.CommodityPrices.Add(created);
                audit.Created("CommodityPrice", created.Id);
                continue;
            }

            matched.Add(existing.Id);

            if (existing.PriceAt == transaction.OccurredAt && existing.Rate == rate)
                continue;

            audit.Track<CommodityPriceSnapshot, CommodityPrice>(existing, "CommodityPrice", existing.Id);

            if (existing.PriceAt != transaction.OccurredAt)
            {
                await EnsureNoTransactionCollisionAsync(split.CommodityId, quoteId, transaction.OccurredAt,
                    excludeId: existing.Id, ct);
                existing.PriceAt = transaction.OccurredAt;
            }

            existing.Rate = rate;
        }

        foreach (var stale in existingRows.Where(p => !matched.Contains(p.Id)))
        {
            audit.Track<CommodityPriceSnapshot, CommodityPrice>(stale, "CommodityPrice", stale.Id);
            audit.Deleted("CommodityPrice", stale.Id);
            db.CommodityPrices.Remove(stale);
        }
    }

    public async Task AuditCascadeDeletesAsync(Guid transactionId, CancellationToken ct)
    {
        var ids = await db.CommodityPrices
            .Where(p => p.CreatedByTransactionId == transactionId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        foreach (var id in ids)
            audit.Deleted("CommodityPrice", id);
    }

    private static void ValidateUniqueTargets(IReadOnlyList<AccountTransactionSplit> annotatedSplits)
    {
        var seen = new HashSet<(Guid, Guid)>();
        foreach (var split in annotatedSplits)
        {
            var key = (split.CommodityId, split.UnitPriceCommodityId!.Value);
            if (!seen.Add(key))
                throw new BusinessRuleException(
                    "Two splits in the same transaction cannot carry annotations targeting the same "
                    + "(commodity, quote currency) pair.");
        }
    }

    private async Task EnsureNoTransactionCollisionAsync(Guid commodityId, Guid quoteCurrencyId,
        DateTimeOffset priceAt, Guid? excludeId, CancellationToken ct)
    {
        var exists = await db.CommodityPrices
            .AsNoTracking()
            .AnyAsync(p => p.CommodityId == commodityId
                        && p.QuoteCurrencyId == quoteCurrencyId
                        && p.PriceAt == priceAt
                        && p.Source == CommodityPriceSource.Transaction
                        && (excludeId == null || p.Id != excludeId), ct);

        if (exists)
            throw new BusinessRuleException(
                $"A transaction-linked price for this commodity already exists at {priceAt:u}. "
                + "Two transactions can't both set the price for the same commodity at the same instant — "
                + "change the date or time on one of them, or edit the existing price.");
    }
}
