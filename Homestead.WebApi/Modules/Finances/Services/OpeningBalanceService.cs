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
    ILinkedPriceManager linkedPrices,
    IAuditContext audit) : IOpeningBalanceService
{
    public async Task<AccountTransaction> CreateAsync(CreateOpeningBalanceRequest request, CancellationToken ct)
    {
        var account = await FindAccountAsync(request.AccountId, ct);
        AccountWritability.EnsureUserWritable(account);
        ValidateCommodityForAccount(account, request.CommodityId);

        var commodity = await db.Commodities.FindAsync([request.CommodityId], ct)
            ?? throw new BusinessRuleException("Commodity not found.");

        await EnsureNoExistingOpeningBalanceAsync(request.AccountId, request.CommodityId, ct);

        var occurredAt = ResolveOccurredAt(account, request.AsOfDate);
        var transactionId = Guid.NewGuid();

        var signedQuantity = account.AccountType.ApplySign(request.Quantity);
        var openingBalances = await systemAccounts.GetOpeningBalancesAsync(ct);

        var accountSplit = new AccountTransactionSplit
        {
            Id = Guid.NewGuid(),
            AccountId = request.AccountId,
            CommodityId = request.CommodityId,
            Quantity = signedQuantity,
        };
        var equitySplit = new AccountTransactionSplit
        {
            Id = Guid.NewGuid(),
            AccountId = openingBalances.Id,
        };

        ApplyShape(account, commodity, request.ValuePerUnit, accountSplit, equitySplit);

        var transaction = new OpeningBalanceTransaction
        {
            Id = transactionId,
            OccurredAt = occurredAt,
            Memo = NormalizeMemo(request.Memo),
            Type = TransactionType.OpeningBalance,
            Splits = [accountSplit, equitySplit],
        };

        await validator.ValidateAsync(transaction, ct);

        db.AccountTransactions.Add(transaction);
        audit.Created("AccountTransaction", transaction.Id);

        await linkedPrices.SyncForTransactionAsync(transaction, ct);

        return transaction;
    }

    public async Task<AccountTransaction> UpdateAsync(Guid transactionId, UpdateOpeningBalanceRequest request,
        CancellationToken ct)
    {
        var transaction = await db.AccountTransactions
            .OfType<OpeningBalanceTransaction>()
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw new EntityNotFoundException("Opening-balance transaction not found.");

        var openingBalances = await systemAccounts.GetOpeningBalancesAsync(ct);
        var (accountSplit, equitySplit) = SplitOpeningBalance(transaction, openingBalances.Id);

        var account = await FindAccountAsync(accountSplit.AccountId, ct);
        AccountWritability.EnsureUserWritable(account);

        var newOccurredAt = ResolveOccurredAt(account, request.AsOfDate);

        if (newOccurredAt != transaction.OccurredAt)
            await EnsureOpeningBalanceRemainsEarliestAsync(account.Id, accountSplit.CommodityId,
                transaction.Id, newOccurredAt, ct);

        var commodity = await db.Commodities.FindAsync([accountSplit.CommodityId], ct)
            ?? throw new BusinessRuleException("Commodity not found.");

        audit.Track<AccountTransactionSnapshot, AccountTransaction>(
            transaction, "AccountTransaction", transaction.Id);

        transaction.OccurredAt = newOccurredAt;
        transaction.Memo = NormalizeMemo(request.Memo);

        accountSplit.Quantity = account.AccountType.ApplySign(request.Quantity);

        ApplyShape(account, commodity, request.ValuePerUnit, accountSplit, equitySplit);

        await validator.ValidateAsync(transaction, ct);

        await linkedPrices.SyncForTransactionAsync(transaction, ct);

        return transaction;
    }

    public async Task<IReadOnlyCollection<TouchedPair>> DeleteAsync(Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.AccountTransactions
            .OfType<OpeningBalanceTransaction>()
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw new EntityNotFoundException("Opening-balance transaction not found.");

        var touched = transaction.Splits
            .Select(s => new TouchedPair(s.AccountId, s.CommodityId, transaction.OccurredAt))
            .ToArray();

        await linkedPrices.AuditCascadeDeletesAsync(transactionId, ct);

        db.AccountTransactions.Remove(transaction);
        audit.Deleted("AccountTransaction", transaction.Id, transaction.Memo);

        return touched;
    }

    private async Task<Account> FindAccountAsync(Guid accountId, CancellationToken ct)
    {
        return await db.Accounts.FindAsync([accountId], ct)
            ?? throw new EntityNotFoundException("Account not found.");
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

    private static string NormalizeMemo(string? memo) => memo?.Trim() ?? "";

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
        return asOfDate.ToUtc(time);
    }

    // Sets the account + equity splits to their final shape for the given (commodity, valuePerUnit):
    //   currency or unpriced security → both splits same commodity, no annotation;
    //   priced security → security leg annotated in reporting currency, equity leg in reporting currency.
    private static void ApplyShape(Account account, Commodity commodity, decimal? valuePerUnit,
        AccountTransactionSplit accountSplit, AccountTransactionSplit equitySplit)
    {
        if (commodity.Kind == CommodityKind.Currency || valuePerUnit is null)
        {
            accountSplit.UnitPriceRate = null;
            accountSplit.UnitPriceCommodityId = null;
            equitySplit.CommodityId = commodity.Id;
            equitySplit.Quantity = -accountSplit.Quantity;
            equitySplit.UnitPriceRate = null;
            equitySplit.UnitPriceCommodityId = null;
            return;
        }

        var quoteCurrencyId = account.ReportingCurrencyId;
        accountSplit.UnitPriceRate = valuePerUnit.Value;
        accountSplit.UnitPriceCommodityId = quoteCurrencyId;
        equitySplit.CommodityId = quoteCurrencyId;
        equitySplit.Quantity = -accountSplit.Quantity * valuePerUnit.Value;
        equitySplit.UnitPriceRate = null;
        equitySplit.UnitPriceCommodityId = null;
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
