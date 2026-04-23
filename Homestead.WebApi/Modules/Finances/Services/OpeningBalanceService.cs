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

        var (txnCurrencyId, effectiveRate) = await ResolveCreateRateAsync(
            account, commodity, valuePerUnit, asOfDate, ct);

        var (accountSplitQuantity, accountSplitValue) = ComputeSignedSplit(
            account, commodity, quantity, effectiveRate);

        var equitySplitCommodityId = commodity.Kind == CommodityKind.Currency
            ? commodityId
            : account.ReportingCurrencyId;

        var openingBalances = await systemAccounts.GetOpeningBalancesAsync(ct);

        var transaction = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            TxnDate = asOfDate,
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

        if (asOfDate != transaction.TxnDate)
            await EnsureOpeningBalanceRemainsEarliestAsync(account.Id, accountSplit.CommodityId,
                transaction.Id, asOfDate, ct);

        var commodity = await db.Commodities.FindAsync([accountSplit.CommodityId], ct)
            ?? throw new BusinessRuleException("Commodity not found.");

        audit.Track<AccountTransactionSnapshot, AccountTransaction>(
            transaction, "AccountTransaction", transaction.Id);

        var (txnCurrencyId, effectiveRate) = await ResolveUpdateRateAsync(
            account, commodity, oldDate: transaction.TxnDate, newDate: asOfDate, valuePerUnit, ct);

        var (newAccountQuantity, newAccountValue) = ComputeSignedSplit(
            account, commodity, quantity, effectiveRate);

        transaction.TxnDate = asOfDate;
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
        Guid openingBalanceTransactionId, DateOnly newDate, CancellationToken ct)
    {
        var earliestOther = await db.AccountTransactionSplits
            .Where(s => s.AccountId == accountId && s.CommodityId == commodityId
                     && s.TransactionId != openingBalanceTransactionId)
            .Join(db.AccountTransactions,
                s => s.TransactionId,
                t => t.Id,
                (s, t) => (DateOnly?)t.TxnDate)
            .MinAsync(ct);

        if (earliestOther is not null && newDate > earliestOther.Value)
            throw new BusinessRuleException(
                $"Opening balance must remain the earliest transaction for this account and commodity "
                + $"(earliest other activity is {earliestOther.Value:yyyy-MM-dd}).");
    }

    private async Task<(Guid txnCurrencyId, decimal effectiveRate)> ResolveCreateRateAsync(
        Account account, Commodity commodity, decimal? valuePerUnit, DateOnly asOfDate,
        CancellationToken ct)
    {
        if (commodity.Kind == CommodityKind.Currency)
            return (commodity.Id, 1m);

        var quoteCurrencyId = account.ReportingCurrencyId;

        var existingPrice = await db.CommodityPrices
            .FirstOrDefaultAsync(p => p.CommodityId == commodity.Id
                                   && p.QuoteCurrencyId == quoteCurrencyId
                                   && p.PriceDate == asOfDate, ct);

        if (existingPrice is not null)
            return (quoteCurrencyId, existingPrice.Rate);

        if (valuePerUnit is null)
            throw new BusinessRuleException(
                "Price per unit is required for security positions with no existing price on the given date.");

        AddCommodityPrice(commodity.Id, quoteCurrencyId, asOfDate, valuePerUnit.Value);
        return (quoteCurrencyId, valuePerUnit.Value);
    }

    // Skill: the old date's price row is left alone on date change (carry-forward). Only the
    // target date's row is managed here — either updated in place (same date) or created fresh.
    private async Task<(Guid txnCurrencyId, decimal effectiveRate)> ResolveUpdateRateAsync(
        Account account, Commodity commodity, DateOnly oldDate, DateOnly newDate,
        decimal? valuePerUnit, CancellationToken ct)
    {
        if (commodity.Kind == CommodityKind.Currency)
            return (commodity.Id, 1m);

        var quoteCurrencyId = account.ReportingCurrencyId;

        if (oldDate == newDate)
        {
            var existing = await db.CommodityPrices
                .FirstOrDefaultAsync(p => p.CommodityId == commodity.Id
                                       && p.QuoteCurrencyId == quoteCurrencyId
                                       && p.PriceDate == newDate, ct);

            if (existing is not null)
            {
                if (valuePerUnit is decimal newRate && existing.Rate != newRate)
                {
                    audit.Track<CommodityPriceSnapshot, CommodityPrice>(
                        existing, "CommodityPrice", existing.Id);
                    existing.Rate = newRate;
                    return (quoteCurrencyId, newRate);
                }
                return (quoteCurrencyId, existing.Rate);
            }

            if (valuePerUnit is null)
                throw new BusinessRuleException(
                    "Price per unit is required — no price exists for this security on the given date.");

            AddCommodityPrice(commodity.Id, quoteCurrencyId, newDate, valuePerUnit.Value);
            return (quoteCurrencyId, valuePerUnit.Value);
        }

        // Date changed: target must be clear; old-date row is intentionally left alone.
        var collision = await db.CommodityPrices
            .AnyAsync(p => p.CommodityId == commodity.Id
                        && p.QuoteCurrencyId == quoteCurrencyId
                        && p.PriceDate == newDate, ct);
        if (collision)
            throw new BusinessRuleException(
                $"A price for this security already exists on {newDate:yyyy-MM-dd}. "
                + "Remove that price manually before moving the opening balance.");

        if (valuePerUnit is null)
            throw new BusinessRuleException(
                "Price per unit is required when moving a security opening balance to a new date.");

        AddCommodityPrice(commodity.Id, quoteCurrencyId, newDate, valuePerUnit.Value);
        return (quoteCurrencyId, valuePerUnit.Value);
    }

    private void AddCommodityPrice(Guid commodityId, Guid quoteCurrencyId, DateOnly date, decimal rate)
    {
        var price = new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = commodityId,
            QuoteCurrencyId = quoteCurrencyId,
            PriceDate = date,
            Rate = rate,
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
