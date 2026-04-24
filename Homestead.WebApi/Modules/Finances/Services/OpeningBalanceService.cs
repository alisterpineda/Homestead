using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public class OpeningBalanceService(
    AppDbContext db,
    ISystemAccountProvider systemAccounts,
    ITransactionValidator validator,
    IAuditContext audit) : IOpeningBalanceService
{
    public async Task<AccountTransaction> CreateAsync(Guid accountId, Guid commodityId, decimal quantity,
        decimal? valuePerUnit, DateOnly asOfDate, CancellationToken ct)
    {
        var account = await FindAccountAsync(accountId, ct);
        ValidateAccountIsWritable(account);
        ValidateCommodityForAccount(account, commodityId);

        var commodity = await db.Commodities.FindAsync([commodityId], ct)
            ?? throw new BusinessRuleException("Commodity not found.");

        await EnsureNoExistingOpeningBalanceAsync(accountId, commodityId, ct);

        var occurredAt = ResolveOccurredAt(account, asOfDate);
        var transactionId = Guid.NewGuid();

        var (txnCurrencyId, effectiveRate) = await ResolveCreateRateAsync(
            account, commodity, valuePerUnit, occurredAt, transactionId, ct);

        var (accountSplitQuantity, accountSplitValue) = ComputeSignedSplit(
            account, commodity, quantity, effectiveRate);

        var equitySplitCommodityId = commodity.Kind == CommodityKind.Currency
            ? commodityId
            : account.ReportingCurrencyId;

        var openingBalances = await systemAccounts.GetOpeningBalancesAsync(ct);

        var transaction = new NormalTransaction
        {
            Id = transactionId,
            OccurredAt = occurredAt,
            Description = OpeningBalanceTransactions.Description,
            CurrencyId = txnCurrencyId,
            Type = TransactionType.Normal,
            Splits =
            [
                new AccountTransactionSplit
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    CommodityId = commodityId,
                    Quantity = accountSplitQuantity,
                    Value = accountSplitValue,
                },
                new AccountTransactionSplit
                {
                    Id = Guid.NewGuid(),
                    AccountId = openingBalances.Id,
                    CommodityId = equitySplitCommodityId,
                    Quantity = -accountSplitValue,
                    Value = -accountSplitValue,
                },
            ],
        };

        validator.Validate(transaction);

        db.AccountTransactions.Add(transaction);
        audit.Created("AccountTransaction", transaction.Id);

        return transaction;
    }

    public async Task<AccountTransaction> UpdateAsync(Guid transactionId, decimal quantity,
        decimal? valuePerUnit, DateOnly asOfDate, CancellationToken ct)
    {
        var transaction = await db.AccountTransactions
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw new EntityNotFoundException("Opening-balance transaction not found.");

        if (transaction.Description != OpeningBalanceTransactions.Description)
            throw new BusinessRuleException("Transaction is not an opening balance.");

        var openingBalances = await systemAccounts.GetOpeningBalancesAsync(ct);
        var (accountSplit, equitySplit) = SplitOpeningBalance(transaction, openingBalances.Id);

        var account = await FindAccountAsync(accountSplit.AccountId, ct);
        ValidateAccountIsWritable(account);

        var newOccurredAt = ResolveOccurredAt(account, asOfDate);

        if (newOccurredAt != transaction.OccurredAt)
            await EnsureOpeningBalanceRemainsEarliestAsync(account.Id, accountSplit.CommodityId,
                transaction.Id, newOccurredAt, ct);

        var commodity = await db.Commodities.FindAsync([accountSplit.CommodityId], ct)
            ?? throw new BusinessRuleException("Commodity not found.");

        audit.Track<AccountTransactionSnapshot, AccountTransaction>(
            transaction, "AccountTransaction", transaction.Id);

        var (txnCurrencyId, effectiveRate) = await ResolveUpdateRateAsync(
            account, commodity, transaction, newOccurredAt, valuePerUnit, ct);

        var (newAccountQuantity, newAccountValue) = ComputeSignedSplit(
            account, commodity, quantity, effectiveRate);

        transaction.OccurredAt = newOccurredAt;
        transaction.CurrencyId = txnCurrencyId;

        accountSplit.Quantity = newAccountQuantity;
        accountSplit.Value = newAccountValue;
        equitySplit.Quantity = -newAccountValue;
        equitySplit.Value = -newAccountValue;

        validator.Validate(transaction);

        return transaction;
    }

    public async Task DeleteAsync(Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.AccountTransactions
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw new EntityNotFoundException("Opening-balance transaction not found.");

        if (transaction.Description != OpeningBalanceTransactions.Description)
            throw new BusinessRuleException("Transaction is not an opening balance.");

        var openingBalances = await systemAccounts.GetOpeningBalancesAsync(ct);
        var (accountSplit, _) = SplitOpeningBalance(transaction, openingBalances.Id);

        var otherActivity = await db.AccountTransactionSplits
            .AnyAsync(s => s.AccountId == accountSplit.AccountId
                        && s.CommodityId == accountSplit.CommodityId
                        && s.TransactionId != transactionId, ct);
        if (otherActivity)
            throw new BusinessRuleException(
                "Opening balance cannot be deleted while other transactions reference this account and commodity.");

        // Audit entry for the linked price row — otherwise the FK cascade delete is silent in the log.
        var linkedPriceId = await db.CommodityPrices
            .Where(p => p.CreatedByTransactionId == transactionId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (linkedPriceId is Guid priceId)
            audit.Deleted("CommodityPrice", priceId);

        db.AccountTransactions.Remove(transaction);
        audit.Deleted("AccountTransaction", transaction.Id, OpeningBalanceTransactions.Description);
    }

    private async Task<Account> FindAccountAsync(Guid accountId, CancellationToken ct)
    {
        return await db.Accounts.FindAsync([accountId], ct)
            ?? throw new EntityNotFoundException("Account not found.");
    }

    private static void ValidateAccountIsWritable(Account account)
    {
        if (account.IsSystem)
            throw new BusinessRuleException("System accounts cannot have opening balances.");
        if (account.IsArchived)
            throw new BusinessRuleException("Archived accounts cannot be modified.");
    }

    // Non-Investment accounts are single-position: the commodity must be the reporting currency.
    private static void ValidateCommodityForAccount(Account account, Guid commodityId)
    {
        if (account.Kind != AccountKind.Investment && commodityId != account.ReportingCurrencyId)
            throw new BusinessRuleException(
                "For this account kind, the opening-balance commodity must match the reporting currency.");
    }

    private async Task EnsureNoExistingOpeningBalanceAsync(Guid accountId, Guid commodityId,
        CancellationToken ct)
    {
        var exists = await db.OpeningBalancesFor(accountId, commodityId).AnyAsync(ct);
        if (exists)
            throw new BusinessRuleException(
                "An opening balance already exists for this account and commodity. Edit it instead.");
    }

    private async Task EnsureOpeningBalanceRemainsEarliestAsync(Guid accountId, Guid commodityId,
        Guid openingBalanceTransactionId, DateTimeOffset newOccurredAt, CancellationToken ct)
    {
        var earliestOther = await db.AccountTransactionSplits
            .Where(s => s.AccountId == accountId && s.CommodityId == commodityId
                     && s.TransactionId != openingBalanceTransactionId)
            .Join(db.AccountTransactions,
                s => s.TransactionId,
                t => t.Id,
                (s, t) => (DateTimeOffset?)t.OccurredAt)
            .MinAsync(ct);

        if (earliestOther is not null && newOccurredAt > earliestOther.Value)
            throw new BusinessRuleException(
                $"Opening balance must remain the earliest transaction for this account and commodity "
                + $"(earliest other activity is {DateOnly.FromDateTime(earliestOther.Value.UtcDateTime):yyyy-MM-dd}).");
    }

    // Investment accounts collect opening balances through Add position (shape: 12:00 UTC).
    // Non-Investment accounts collect a single opening balance at account creation (00:00 UTC).
    private static DateTimeOffset ResolveOccurredAt(Account account, DateOnly asOfDate)
    {
        var time = account.Kind == AccountKind.Investment ? new TimeOnly(12, 0) : TimeOnly.MinValue;
        return new DateTimeOffset(asOfDate.ToDateTime(time), TimeSpan.Zero);
    }

    private async Task<(Guid txnCurrencyId, decimal effectiveRate)> ResolveCreateRateAsync(
        Account account, Commodity commodity, decimal? valuePerUnit, DateTimeOffset occurredAt,
        Guid transactionId, CancellationToken ct)
    {
        if (commodity.Kind == CommodityKind.Currency)
            return (commodity.Id, 1m);

        var quoteCurrencyId = account.ReportingCurrencyId;

        // No price supplied: capture the position unpriced. Both splits book Value=0;
        // net-worth rendering relies on carry-forward (or marks the position *unpriced*).
        if (valuePerUnit is null)
            return (quoteCurrencyId, 0m);

        await ThrowIfPriceExistsAtAsync(commodity.Id, quoteCurrencyId, occurredAt, excludeId: null, ct);
        AddCommodityPrice(commodity.Id, quoteCurrencyId, occurredAt, valuePerUnit.Value, transactionId);
        return (quoteCurrencyId, valuePerUnit.Value);
    }

    // The linked price row is tracked by CreatedByTransactionId: rate updates in place,
    // date changes move PriceAt on the same row, and deletes cascade via FK.
    private async Task<(Guid txnCurrencyId, decimal effectiveRate)> ResolveUpdateRateAsync(
        Account account, Commodity commodity, AccountTransaction transaction,
        DateTimeOffset newOccurredAt, decimal? valuePerUnit, CancellationToken ct)
    {
        if (commodity.Kind == CommodityKind.Currency)
            return (commodity.Id, 1m);

        var quoteCurrencyId = account.ReportingCurrencyId;

        var linkedPrice = await db.CommodityPrices
            .FirstOrDefaultAsync(p => p.CreatedByTransactionId == transaction.Id, ct);

        if (linkedPrice is null)
        {
            if (valuePerUnit is null)
                return (quoteCurrencyId, 0m);

            await ThrowIfPriceExistsAtAsync(commodity.Id, quoteCurrencyId, newOccurredAt, excludeId: null, ct);
            AddCommodityPrice(commodity.Id, quoteCurrencyId, newOccurredAt,
                valuePerUnit.Value, transaction.Id);
            return (quoteCurrencyId, valuePerUnit.Value);
        }

        audit.Track<CommodityPriceSnapshot, CommodityPrice>(
            linkedPrice, "CommodityPrice", linkedPrice.Id);

        if (newOccurredAt != linkedPrice.PriceAt)
        {
            await ThrowIfPriceExistsAtAsync(commodity.Id, quoteCurrencyId, newOccurredAt,
                excludeId: linkedPrice.Id, ct);
            linkedPrice.PriceAt = newOccurredAt;
        }

        if (valuePerUnit is decimal newRate)
            linkedPrice.Rate = newRate;

        return (quoteCurrencyId, linkedPrice.Rate);
    }

    private async Task ThrowIfPriceExistsAtAsync(Guid commodityId, Guid quoteCurrencyId,
        DateTimeOffset priceAt, Guid? excludeId, CancellationToken ct)
    {
        var exists = await db.CommodityPrices
            .AsNoTracking()
            .AnyAsync(p => p.CommodityId == commodityId
                        && p.QuoteCurrencyId == quoteCurrencyId
                        && p.PriceAt == priceAt
                        && (excludeId == null || p.Id != excludeId), ct);
        if (!exists) return;

        throw new BusinessRuleException(
            $"A price for this security already exists at {DateOnly.FromDateTime(priceAt.UtcDateTime):yyyy-MM-dd}. "
            + "Remove or move that price row before modifying the opening balance.");
    }

    private void AddCommodityPrice(Guid commodityId, Guid quoteCurrencyId, DateTimeOffset priceAt,
        decimal rate, Guid createdByTransactionId)
    {
        var price = new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = commodityId,
            QuoteCurrencyId = quoteCurrencyId,
            PriceAt = priceAt,
            Rate = rate,
            CreatedByTransactionId = createdByTransactionId,
        };
        db.CommodityPrices.Add(price);
        audit.Created("CommodityPrice", price.Id);
    }

    private static (decimal quantity, decimal value) ComputeSignedSplit(
        Account account, Commodity commodity, decimal quantity, decimal rate)
    {
        var value = commodity.Kind == CommodityKind.Currency ? quantity : quantity * rate;

        if (account.AccountType == AccountType.Liability)
            return (-quantity, -value);

        return (quantity, value);
    }

    private static (AccountTransactionSplit accountSplit, AccountTransactionSplit equitySplit)
        SplitOpeningBalance(AccountTransaction transaction, Guid openingBalancesAccountId)
    {
        if (transaction.Splits.Count != 2)
            throw new BusinessRuleException("Opening-balance transaction must have exactly two splits.");

        var equitySplit = transaction.Splits.FirstOrDefault(s => s.AccountId == openingBalancesAccountId)
            ?? throw new BusinessRuleException(
                "Opening-balance transaction does not reference Equity:Opening Balances.");
        var accountSplit = transaction.Splits.First(s => s.Id != equitySplit.Id);

        return (accountSplit, equitySplit);
    }
}
