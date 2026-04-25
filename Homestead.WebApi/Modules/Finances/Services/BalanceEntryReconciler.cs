using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public class BalanceEntryReconciler(
    AppDbContext db,
    ISystemAccountProvider systemAccounts,
    IAuditContext audit) : IBalanceEntryReconciler
{
    public async Task ReconcileAsync(IEnumerable<TouchedPair> touched, CancellationToken ct)
    {
        // Collapse to one entry per (account, commodity) pair with the earliest affected instant.
        var pairs = touched
            .GroupBy(t => (t.AccountId, t.CommodityId))
            .Select(g => (g.Key.AccountId, g.Key.CommodityId,
                          EarliestAffected: g.Min(t => t.EarliestAffected)))
            .ToList();

        if (pairs.Count == 0) return;

        var adjustments = await systemAccounts.GetAdjustmentsAsync(ct);

        // Tracks in-memory split-quantity overrides applied by this reconciliation pass so that
        // later iterations' prior-running calculations see them (DB queries via SumAsync go to
        // the database and won't reflect tracked-but-unsaved Modified entities).
        var splitOverrides = new Dictionary<Guid, decimal>();

        foreach (var (accountId, commodityId, earliestAffected) in pairs)
        {
            var entries = await db.AccountTransactions
                .OfType<BalanceEntryTransaction>()
                .Include(e => e.Splits)
                .Where(e => e.AccountId == accountId
                         && e.AssertedCommodityId == commodityId
                         && e.OccurredAt >= earliestAffected)
                .OrderBy(e => e.OccurredAt)
                .ThenBy(e => e.CreatedAt)
                .ThenBy(e => e.Id)
                .ToListAsync(ct);

            foreach (var entry in entries)
                await ReconcileEntryAsync(entry, adjustments.Id, splitOverrides, ct);
        }
    }

    private async Task ReconcileEntryAsync(
        BalanceEntryTransaction entry,
        Guid adjustmentsAccountId,
        Dictionary<Guid, decimal> splitOverrides,
        CancellationToken ct)
    {
        var prior = await ComputePriorRunningAsync(entry, splitOverrides, ct);
        var delta = entry.AssertedQuantity - prior;

        var (accountSplit, adjustmentsSplit) = EnsureSplits(entry, adjustmentsAccountId);

        if (accountSplit.Quantity == delta && adjustmentsSplit.Quantity == -delta)
        {
            // Splits already match — record the overrides anyway so later entries see consistent state.
            splitOverrides[accountSplit.Id] = accountSplit.Quantity;
            splitOverrides[adjustmentsSplit.Id] = adjustmentsSplit.Quantity;
            return;
        }

        audit.Track<AccountTransactionSnapshot, AccountTransaction>(
            entry, "AccountTransaction", entry.Id);

        accountSplit.Quantity = delta;
        adjustmentsSplit.Quantity = -delta;

        splitOverrides[accountSplit.Id] = delta;
        splitOverrides[adjustmentsSplit.Id] = -delta;
    }

    private async Task<decimal> ComputePriorRunningAsync(
        BalanceEntryTransaction entry,
        IReadOnlyDictionary<Guid, decimal> splitOverrides,
        CancellationToken ct)
    {
        var entrySplitIds = entry.Splits.Select(s => s.Id).ToHashSet();

        var splits = await db.AccountTransactionSplits
            .AsNoTracking()
            .Where(s => s.AccountId == entry.AccountId
                     && s.CommodityId == entry.AssertedCommodityId
                     && !entrySplitIds.Contains(s.Id)
                     && s.Transaction.OccurredAt < entry.OccurredAt)
            .Select(s => new { s.Id, s.Quantity })
            .ToListAsync(ct);

        return splits.Sum(s =>
            splitOverrides.TryGetValue(s.Id, out var modified) ? modified : s.Quantity);
    }

    private static (AccountTransactionSplit accountSplit, AccountTransactionSplit adjustmentsSplit)
        EnsureSplits(BalanceEntryTransaction entry, Guid adjustmentsAccountId)
    {
        if (entry.Splits.Count != 2)
            throw new InvalidOperationException(
                $"Balance entry {entry.Id} has {entry.Splits.Count} splits; expected 2.");

        var adjustments = entry.Splits.FirstOrDefault(s => s.AccountId == adjustmentsAccountId)
            ?? throw new InvalidOperationException(
                $"Balance entry {entry.Id} has no split on Equity:Adjustments.");
        var account = entry.Splits.First(s => s.Id != adjustments.Id);

        return (account, adjustments);
    }
}
