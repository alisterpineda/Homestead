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

        var signedQuantity = SignedQuantity(account, quantity);
        var openingBalances = await systemAccounts.GetOpeningBalancesAsync(ct);

        var accountSplit = new AccountTransactionSplit
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            CommodityId = commodityId,
            Quantity = signedQuantity,
        };
        var equitySplit = new AccountTransactionSplit
        {
            Id = Guid.NewGuid(),
            AccountId = openingBalances.Id,
        };

        await ApplyCreateShapeAsync(
            account, commodity, valuePerUnit, occurredAt, transactionId,
            accountSplit, equitySplit, ct);

        var transaction = new NormalTransaction
        {
            Id = transactionId,
            OccurredAt = occurredAt,
            Description = OpeningBalanceTransactions.Description,
            Type = TransactionType.Normal,
            Splits = [accountSplit, equitySplit],
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

        transaction.OccurredAt = newOccurredAt;

        var signedQuantity = SignedQuantity(account, quantity);
        accountSplit.Quantity = signedQuantity;

        await ApplyUpdateShapeAsync(
            account, commodity, valuePerUnit, newOccurredAt, transaction.Id,
            accountSplit, equitySplit, ct);

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

    private async Task ApplyCreateShapeAsync(
        Account account, Commodity commodity, decimal? valuePerUnit, DateTimeOffset occurredAt,
        Guid transactionId, AccountTransactionSplit accountSplit, AccountTransactionSplit equitySplit,
        CancellationToken ct)
    {
        // Currency or unpriced security: both splits in the same commodity, no annotation, no price row.
        if (commodity.Kind == CommodityKind.Currency || valuePerUnit is null)
        {
            SetUnpricedSplit(equitySplit, commodity.Id, -accountSplit.Quantity);
            return;
        }

        // Priced security: annotation on the security leg, equity leg in reporting currency,
        // linked CommodityPrice row emitted.
        var quoteCurrencyId = account.ReportingCurrencyId;
        await ThrowIfPriceExistsAtAsync(commodity.Id, quoteCurrencyId, occurredAt, excludeId: null, ct);

        accountSplit.UnitPriceRate = valuePerUnit.Value;
        accountSplit.UnitPriceCommodityId = quoteCurrencyId;
        SetUnpricedSplit(equitySplit, quoteCurrencyId, -accountSplit.Quantity * valuePerUnit.Value);

        AddCommodityPrice(commodity.Id, quoteCurrencyId, occurredAt, valuePerUnit.Value, transactionId);
    }

    // Handles priced ↔ unpriced transitions: the equity leg's commodity flips between the
    // security and the reporting currency, and the linked price row is created/removed.
    private async Task ApplyUpdateShapeAsync(
        Account account, Commodity commodity, decimal? valuePerUnit, DateTimeOffset newOccurredAt,
        Guid transactionId, AccountTransactionSplit accountSplit, AccountTransactionSplit equitySplit,
        CancellationToken ct)
    {
        if (commodity.Kind == CommodityKind.Currency)
        {
            ClearAnnotation(accountSplit);
            SetUnpricedSplit(equitySplit, commodity.Id, -accountSplit.Quantity);
            return;
        }

        var quoteCurrencyId = account.ReportingCurrencyId;
        var linkedPrice = await db.CommodityPrices
            .FirstOrDefaultAsync(p => p.CreatedByTransactionId == transactionId, ct);

        if (valuePerUnit is null)
        {
            if (linkedPrice is not null)
            {
                audit.Track<CommodityPriceSnapshot, CommodityPrice>(
                    linkedPrice, "CommodityPrice", linkedPrice.Id);
                audit.Deleted("CommodityPrice", linkedPrice.Id);
                db.CommodityPrices.Remove(linkedPrice);
            }

            ClearAnnotation(accountSplit);
            SetUnpricedSplit(equitySplit, commodity.Id, -accountSplit.Quantity);
            return;
        }

        if (linkedPrice is null)
        {
            await ThrowIfPriceExistsAtAsync(commodity.Id, quoteCurrencyId, newOccurredAt, excludeId: null, ct);
            AddCommodityPrice(commodity.Id, quoteCurrencyId, newOccurredAt, valuePerUnit.Value, transactionId);
        }
        else
        {
            audit.Track<CommodityPriceSnapshot, CommodityPrice>(
                linkedPrice, "CommodityPrice", linkedPrice.Id);

            if (newOccurredAt != linkedPrice.PriceAt)
            {
                await ThrowIfPriceExistsAtAsync(commodity.Id, quoteCurrencyId, newOccurredAt,
                    excludeId: linkedPrice.Id, ct);
                linkedPrice.PriceAt = newOccurredAt;
            }

            linkedPrice.Rate = valuePerUnit.Value;
        }

        accountSplit.UnitPriceRate = valuePerUnit.Value;
        accountSplit.UnitPriceCommodityId = quoteCurrencyId;
        SetUnpricedSplit(equitySplit, quoteCurrencyId, -accountSplit.Quantity * valuePerUnit.Value);
    }

    private static void SetUnpricedSplit(
        AccountTransactionSplit split, Guid commodityId, decimal quantity)
    {
        split.CommodityId = commodityId;
        split.Quantity = quantity;
        split.UnitPriceRate = null;
        split.UnitPriceCommodityId = null;
    }

    private static void ClearAnnotation(AccountTransactionSplit split)
    {
        split.UnitPriceRate = null;
        split.UnitPriceCommodityId = null;
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

    private static decimal SignedQuantity(Account account, decimal quantity) =>
        account.AccountType == AccountType.Liability ? -quantity : quantity;

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
