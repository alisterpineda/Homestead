using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public class BalanceEntryService(
    AppDbContext db,
    ISystemAccountProvider systemAccounts,
    ITransactionValidator validator,
    IAuditContext audit) : IBalanceEntryService
{
    private const string DefaultDescription = "Balance assertion";

    public async Task<BalanceEntryMutationResult> CreateAsync(
        CreateBalanceEntryRequest request, CancellationToken ct)
    {
        var account = await LoadAccountAsync(request.AccountId, ct);
        var commodity = await LoadCommodityAsync(request.CommodityId, ct);
        ValidateCommodityForAccount(account, commodity);

        var occurredAt = ResolveOccurredAt(request.Date);

        await EnsureNoExistingEntryAsync(account.Id, commodity.Id, occurredAt, excludeId: null, ct);

        var signedAsserted = account.AccountType.ApplySign(request.AssertedAmount);
        var adjustments = await systemAccounts.GetAdjustmentsAsync(ct);

        // Compute prior running quantity from current DB state. The new entry is not yet in
        // the DB, so this query trivially excludes its splits.
        var prior = await ComputePriorRunningAsync(account.Id, commodity.Id, occurredAt, ct);
        var delta = signedAsserted - prior;

        var entry = new BalanceEntryTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = occurredAt,
            Description = NormalizeDescription(request.Description),
            Type = TransactionType.BalanceEntry,
            AccountId = account.Id,
            AssertedCommodityId = commodity.Id,
            AssertedQuantity = signedAsserted,
            Splits =
            [
                new AccountTransactionSplit
                {
                    Id = Guid.NewGuid(),
                    AccountId = account.Id,
                    CommodityId = commodity.Id,
                    Quantity = delta,
                },
                new AccountTransactionSplit
                {
                    Id = Guid.NewGuid(),
                    AccountId = adjustments.Id,
                    CommodityId = commodity.Id,
                    Quantity = -delta,
                },
            ],
        };

        validator.Validate(entry);

        db.AccountTransactions.Add(entry);
        audit.Created("AccountTransaction", entry.Id);

        // Touched pair triggers downstream reconciliation; the new entry itself is reconciled
        // here, so the post-save pass is idempotent for it (its splits already match the delta).
        var touched = new[] { new TouchedPair(account.Id, commodity.Id, occurredAt) };
        return new BalanceEntryMutationResult(entry, touched);
    }

    public async Task<BalanceEntryMutationResult> UpdateAsync(
        Guid transactionId, Guid expectedAccountId, UpdateBalanceEntryRequest request, CancellationToken ct)
    {
        var entry = await LoadEntryAsync(transactionId, ct);

        if (entry.AccountId != expectedAccountId)
            throw new BusinessRuleException("Balance entry does not belong to this account.");

        var account = await LoadAccountAsync(entry.AccountId, ct);
        var newCommodity = await LoadCommodityAsync(request.CommodityId, ct);
        ValidateCommodityForAccount(account, newCommodity);

        var oldCommodityId = entry.AssertedCommodityId;
        var oldOccurredAt = entry.OccurredAt;

        var newOccurredAt = ResolveOccurredAt(request.Date);
        var signedAsserted = account.AccountType.ApplySign(request.AssertedAmount);

        if (newCommodity.Id != oldCommodityId || newOccurredAt != oldOccurredAt)
            await EnsureNoExistingEntryAsync(account.Id, newCommodity.Id, newOccurredAt,
                excludeId: entry.Id, ct);

        audit.Track<AccountTransactionSnapshot, AccountTransaction>(
            entry, "AccountTransaction", entry.Id);

        entry.OccurredAt = newOccurredAt;
        entry.AssertedCommodityId = newCommodity.Id;
        entry.AssertedQuantity = signedAsserted;
        entry.Description = NormalizeDescription(request.Description);

        // Re-bind splits to the (possibly-new) commodity. Reconciliation rewrites quantities.
        var adjustments = await systemAccounts.GetAdjustmentsAsync(ct);
        var adjustmentsSplit = entry.Splits.FirstOrDefault(s => s.AccountId == adjustments.Id)
            ?? throw new InvalidOperationException("Balance entry missing Equity:Adjustments split.");
        var accountSplit = entry.Splits.First(s => s.Id != adjustmentsSplit.Id);
        accountSplit.AccountId = account.Id;
        accountSplit.CommodityId = newCommodity.Id;
        adjustmentsSplit.CommodityId = newCommodity.Id;

        validator.Validate(entry);

        // Touched pairs cover both old and new (account, commodity) chains. Earliest-affected
        // for the new pair is the earlier of old/new instants so any entries between them are
        // recomputed.
        var touched = new List<TouchedPair>
        {
            new(account.Id, newCommodity.Id, MinInstant(oldOccurredAt, newOccurredAt)),
        };
        if (oldCommodityId != newCommodity.Id)
            touched.Add(new TouchedPair(account.Id, oldCommodityId, oldOccurredAt));

        return new BalanceEntryMutationResult(entry, touched);
    }

    public async Task<BalanceEntryMutationResult> DeleteAsync(Guid transactionId, CancellationToken ct)
    {
        var entry = await LoadEntryAsync(transactionId, ct);

        var touched = new[]
        {
            new TouchedPair(entry.AccountId, entry.AssertedCommodityId, entry.OccurredAt),
        };

        db.AccountTransactions.Remove(entry);
        audit.Deleted("AccountTransaction", entry.Id, entry.Description);

        return new BalanceEntryMutationResult(null, touched);
    }

    private async Task<BalanceEntryTransaction> LoadEntryAsync(Guid transactionId, CancellationToken ct)
    {
        return await db.AccountTransactions
            .OfType<BalanceEntryTransaction>()
            .Include(e => e.Splits)
            .FirstOrDefaultAsync(e => e.Id == transactionId, ct)
            ?? throw new EntityNotFoundException("Balance entry not found.");
    }

    private async Task<Account> LoadAccountAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.FindAsync([accountId], ct)
            ?? throw new EntityNotFoundException("Account not found.");

        AccountWritability.EnsureUserWritable(account);
        return account;
    }

    private async Task<Commodity> LoadCommodityAsync(Guid commodityId, CancellationToken ct)
    {
        return await db.Commodities.FindAsync([commodityId], ct)
            ?? throw new EntityNotFoundException("Commodity not found.");
    }

    // Non-Investment accounts are single-position: the asserted commodity must be the reporting currency.
    private static void ValidateCommodityForAccount(Account account, Commodity commodity)
    {
        if (account.Kind != AccountKind.Investment && commodity.Id != account.ReportingCurrencyId)
            throw new BusinessRuleException(
                "For this account kind, the balance-entry commodity must match the reporting currency.");
    }

    private async Task EnsureNoExistingEntryAsync(Guid accountId, Guid commodityId,
        DateTimeOffset occurredAt, Guid? excludeId, CancellationToken ct)
    {
        var exists = await db.AccountTransactions
            .OfType<BalanceEntryTransaction>()
            .AnyAsync(e => e.AccountId == accountId
                        && e.AssertedCommodityId == commodityId
                        && e.OccurredAt == occurredAt
                        && (excludeId == null || e.Id != excludeId), ct);

        if (exists)
            throw new BusinessRuleException(
                "A balance entry already exists for this account, commodity, and date.");
    }

    private async Task<decimal> ComputePriorRunningAsync(Guid accountId, Guid commodityId,
        DateTimeOffset occurredAt, CancellationToken ct)
    {
        return await db.AccountTransactionSplits
            .AsNoTracking()
            .Where(s => s.AccountId == accountId
                     && s.CommodityId == commodityId
                     && s.Transaction.OccurredAt < occurredAt)
            .SumAsync(s => (decimal?)s.Quantity, ct) ?? 0m;
    }

    // Balance entries always land at end-of-day UTC so per-instant uniqueness is per-day uniqueness.
    private static DateTimeOffset ResolveOccurredAt(DateOnly date) => date.ToEndOfDayUtc();

    private static DateTimeOffset MinInstant(DateTimeOffset a, DateTimeOffset b) =>
        a < b ? a : b;

    private static string NormalizeDescription(string? description) =>
        TransactionDescriptions.NormalizeOrDefault(description, DefaultDescription);
}
