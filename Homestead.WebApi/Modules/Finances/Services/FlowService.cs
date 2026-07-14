using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using static Homestead.WebApi.Modules.Finances.Services.TransactionWriteHelpers;

namespace Homestead.WebApi.Modules.Finances.Services;

public class FlowService(
    AppDbContext db,
    IFlowCounterpartResolver counterparts,
    ITransactionValidator validator,
    IAuditContext audit) : IFlowService
{
    public async Task<FlowMutationResult> CreateAsync(CreateFlowRequest request, CancellationToken ct)
    {
        var account = await LoadAccountAsync(request.AccountId, ct);
        var currency = await LoadCurrencyAsync(account.ReportingCurrencyId, ct);
        ValidateInputs(request.Amount);

        // System counterpart (Expense/Income) — do not writability-check it (mirrors BalanceEntryService
        // posting to Equity:Adjustments and DividendService posting to Income).
        var counter = await counterparts.ResolveAsync(request.Direction, null, ct);

        // Beancount sign follows flow direction, so one path works on asset and liability accounts alike:
        // Expense drains the account (−a) and lands on Expense (+a); Income does the reverse.
        var accountQuantity = SignedAccountQuantity(request.Direction, request.Amount);

        var transaction = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = request.Date.ToStartOfDayUtc(),
            Memo = NormalizeMemo(request.Description),
            Type = TransactionType.Normal,
            NormalShape = request.Direction == FlowDirection.Expense
                ? NormalShape.Expense
                : NormalShape.Income,
            Splits =
            [
                new AccountTransactionSplit
                {
                    Id = Guid.NewGuid(),
                    AccountId = account.Id,
                    CommodityId = currency.Id,
                    Quantity = accountQuantity,
                },
                new AccountTransactionSplit
                {
                    Id = Guid.NewGuid(),
                    AccountId = counter.Id,
                    CommodityId = currency.Id,
                    Quantity = -accountQuantity,
                },
            ],
        };

        await validator.ValidateAsync(transaction, ct);

        db.AccountTransactions.Add(transaction);
        audit.Created("AccountTransaction", transaction.Id);

        var touched = TouchedFor(transaction).ToArray();
        return new FlowMutationResult(transaction, touched);
    }

    public async Task<FlowMutationResult> UpdateAsync(
        Guid transactionId, UpdateFlowRequest request, CancellationToken ct)
    {
        var transaction = await LoadFlowAsync(transactionId, ct);

        // Direction is immutable — fixed by the stored shape; the patch carries none.
        var direction = transaction.NormalShape == NormalShape.Expense
            ? FlowDirection.Expense
            : FlowDirection.Income;
        var counter = await counterparts.ResolveAsync(direction, null, ct);

        // LoadFlowAsync guarantees two splits; confirm exactly one is the system counterpart so the
        // slot-identification Singles below can't throw a 500 on a structurally-broken row.
        if (transaction.Splits.Count(s => s.AccountId == counter.Id) != 1)
            throw new BusinessRuleException("Flow transaction is malformed.");

        var accountSlot = transaction.Splits.Single(s => s.AccountId != counter.Id);
        var counterSlot = transaction.Splits.Single(s => s.AccountId == counter.Id);

        if (request.AccountId != accountSlot.AccountId)
            throw new BusinessRuleException("A flow's account cannot be changed.");

        var account = await LoadAccountAsync(accountSlot.AccountId, ct);
        var currency = await LoadCurrencyAsync(account.ReportingCurrencyId, ct);
        ValidateInputs(request.Amount);

        var touchedBefore = TouchedFor(transaction).ToList();

        audit.Track<AccountTransactionSnapshot, AccountTransaction>(
            transaction, "AccountTransaction", transaction.Id);

        transaction.OccurredAt = request.Date.ToStartOfDayUtc();
        transaction.Memo = NormalizeMemo(request.Description);

        var accountQuantity = SignedAccountQuantity(direction, request.Amount);

        accountSlot.AccountId = account.Id;
        accountSlot.CommodityId = currency.Id;
        accountSlot.Quantity = accountQuantity;

        counterSlot.CommodityId = currency.Id;
        counterSlot.Quantity = -accountQuantity;

        await validator.ValidateAsync(transaction, ct);

        var touched = touchedBefore.Concat(TouchedFor(transaction)).ToArray();
        return new FlowMutationResult(transaction, touched);
    }

    private async Task<NormalTransaction> LoadFlowAsync(Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.AccountTransactions
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw new EntityNotFoundException("Flow transaction not found.");

        if (transaction is not NormalTransaction normal ||
            (normal.NormalShape != NormalShape.Expense && normal.NormalShape != NormalShape.Income))
            throw new BusinessRuleException("Transaction is not an expense or income flow.");

        // Defensive: the canonical writer always produces exactly two splits (account + counterpart).
        // Guard so a structurally-broken row yields a clean 400 instead of a 500 from the Singles above.
        if (normal.Splits.Count != 2)
            throw new BusinessRuleException("Flow transaction is malformed.");

        return normal;
    }

    private async Task<Account> LoadAccountAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.FindAsync([accountId], ct)
            ?? throw new EntityNotFoundException("Account not found.");

        AccountWritability.EnsureUserWritable(account);

        if (account.Kind == AccountKind.Investment)
            throw new BusinessRuleException(
                "Expense/income can't be recorded on an investment account; use a dividend or trade.");

        return account;
    }

    private async Task<Commodity> LoadCurrencyAsync(Guid commodityId, CancellationToken ct)
    {
        var commodity = await db.Commodities.FindAsync([commodityId], ct)
            ?? throw new EntityNotFoundException("Reporting currency not found.");

        if (commodity.Kind != CommodityKind.Currency)
            throw new BusinessRuleException(
                "The account's reporting commodity must be a currency (Kind=Currency).");

        return commodity;
    }

    private static void ValidateInputs(decimal amount)
    {
        if (amount <= 0m)
            throw new BusinessRuleException("Flow amount must be greater than zero.");
    }

    private static decimal SignedAccountQuantity(FlowDirection direction, decimal amount) =>
        direction == FlowDirection.Expense ? -amount : amount;
}
