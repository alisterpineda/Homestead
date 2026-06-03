using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using static Homestead.WebApi.Modules.Finances.Services.TransactionWriteHelpers;

namespace Homestead.WebApi.Modules.Finances.Services;

public class DividendService(
    AppDbContext db,
    ISystemAccountProvider systemAccounts,
    ITransactionValidator validator,
    IAuditContext audit) : IDividendService
{
    private const string DefaultDescription = "Dividend";

    public async Task<DividendMutationResult> CreateAsync(CreateDividendRequest request, CancellationToken ct)
    {
        var account = await LoadInvestmentAccountAsync(request.AccountId, ct);
        var payment = await LoadCurrencyAsync(request.PaymentCurrencyId, ct);
        ValidateInputs(request.Amount);

        // System counterpart — do not writability-check it (mirrors BalanceEntryService posting to
        // Equity:Adjustments). Income is negative because income increases (Beancount-style).
        var income = await systemAccounts.GetIncomeAsync(ct);

        var transaction = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = request.OccurredAt,
            Memo = NormalizeDescription(request.Description),
            Type = TransactionType.Normal,
            NormalShape = NormalShape.Dividend,
            Splits =
            [
                new AccountTransactionSplit
                {
                    Id = Guid.NewGuid(),
                    AccountId = account.Id,
                    CommodityId = payment.Id,
                    Quantity = request.Amount,
                },
                new AccountTransactionSplit
                {
                    Id = Guid.NewGuid(),
                    AccountId = income.Id,
                    CommodityId = payment.Id,
                    Quantity = -request.Amount,
                },
            ],
        };

        await validator.ValidateAsync(transaction, ct);

        db.AccountTransactions.Add(transaction);
        audit.Created("AccountTransaction", transaction.Id);

        var touched = TouchedFor(transaction).ToArray();
        return new DividendMutationResult(transaction, touched);
    }

    public async Task<DividendMutationResult> UpdateAsync(
        Guid transactionId, UpdateDividendRequest request, CancellationToken ct)
    {
        var transaction = await LoadDividendAsync(transactionId, ct);
        var income = await systemAccounts.GetIncomeAsync(ct);

        // LoadDividendAsync guarantees two splits; confirm exactly one is the Income counterpart so the
        // slot-identification Singles below can't throw a 500 on a structurally-broken row.
        if (transaction.Splits.Count(s => s.AccountId == income.Id) != 1)
            throw new BusinessRuleException("Dividend transaction is malformed.");

        var accountSlot = transaction.Splits.Single(s => s.AccountId != income.Id);
        var incomeSlot = transaction.Splits.Single(s => s.AccountId == income.Id);

        if (request.AccountId != accountSlot.AccountId)
            throw new BusinessRuleException("A dividend's account cannot be changed.");

        var account = await LoadInvestmentAccountAsync(accountSlot.AccountId, ct);
        var payment = await LoadCurrencyAsync(request.PaymentCurrencyId, ct);
        ValidateInputs(request.Amount);

        var touchedBefore = TouchedFor(transaction).ToList();

        audit.Track<AccountTransactionSnapshot, AccountTransaction>(
            transaction, "AccountTransaction", transaction.Id);

        transaction.OccurredAt = request.OccurredAt;
        transaction.Memo = NormalizeDescription(request.Description);

        accountSlot.AccountId = account.Id;
        accountSlot.CommodityId = payment.Id;
        accountSlot.Quantity = request.Amount;

        incomeSlot.CommodityId = payment.Id;
        incomeSlot.Quantity = -request.Amount;

        await validator.ValidateAsync(transaction, ct);

        var touched = touchedBefore.Concat(TouchedFor(transaction)).ToArray();
        return new DividendMutationResult(transaction, touched);
    }

    private async Task<NormalTransaction> LoadDividendAsync(Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.AccountTransactions
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw new EntityNotFoundException("Dividend transaction not found.");

        if (transaction is not NormalTransaction normal || normal.NormalShape != NormalShape.Dividend)
            throw new BusinessRuleException("Transaction is not a dividend.");

        // Defensive: the canonical writer always produces exactly two splits (account + Income).
        // Guard so a structurally-broken row yields a clean 400 instead of an InvalidOperationException
        // (500) from the slot-identification Singles in UpdateAsync.
        if (normal.Splits.Count != 2)
            throw new BusinessRuleException("Dividend transaction is malformed.");

        return normal;
    }

    private async Task<Account> LoadInvestmentAccountAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.FindAsync([accountId], ct)
            ?? throw new EntityNotFoundException("Account not found.");

        AccountWritability.EnsureUserWritable(account);

        if (account.Kind != AccountKind.Investment)
            throw new BusinessRuleException("Dividends can only be recorded on investment accounts.");

        return account;
    }

    private async Task<Commodity> LoadCurrencyAsync(Guid commodityId, CancellationToken ct)
    {
        var commodity = await db.Commodities.FindAsync([commodityId], ct)
            ?? throw new EntityNotFoundException("Payment currency not found.");

        if (commodity.Kind != CommodityKind.Currency)
            throw new BusinessRuleException("A dividend's payment commodity must be a currency (Kind=Currency).");

        return commodity;
    }

    private static void ValidateInputs(decimal amount)
    {
        if (amount <= 0m)
            throw new BusinessRuleException("Dividend amount must be greater than zero.");
    }

    private static string NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? DefaultDescription : description.Trim();
}
