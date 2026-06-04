using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using static Homestead.WebApi.Modules.Finances.Services.TransactionWriteHelpers;

namespace Homestead.WebApi.Modules.Finances.Services;

public class TradeService(
    AppDbContext db,
    ITransactionValidator validator,
    ILinkedPriceManager linkedPrices,
    IAuditContext audit) : ITradeService
{
    public async Task<TradeMutationResult> CreateAsync(CreateTradeRequest request, CancellationToken ct)
    {
        var account = await LoadInvestmentAccountAsync(request.AccountId, ct);
        var (security, funding) = await ResolveCommoditiesAsync(request.SecurityId, request.FundingCurrencyId, ct);
        ValidateInputs(request.Quantity, request.UnitPrice);

        var (cash, sec) = BuildSplits(account.Id, security.Id, funding.Id,
            request.Side, request.Quantity, request.UnitPrice);

        var transaction = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = request.OccurredAt,
            Memo = NormalizeMemo(request.Memo),
            Type = TransactionType.Normal,
            NormalShape = ToNormalShape(request.Side),
            Splits = [cash, sec],
        };

        await validator.ValidateAsync(transaction, ct);

        db.AccountTransactions.Add(transaction);
        audit.Created("AccountTransaction", transaction.Id);

        await linkedPrices.SyncForTransactionAsync(transaction, ct);

        var touched = TouchedFor(transaction).ToArray();
        return new TradeMutationResult(transaction, touched);
    }

    public async Task<TradeMutationResult> UpdateAsync(
        Guid transactionId, UpdateTradeRequest request, CancellationToken ct)
    {
        var transaction = await LoadTradeAsync(transactionId, ct);

        // A DRIP half is an ordinary InvestmentBuy row, so this editor would otherwise let a user
        // rewrite one half on its own and break the pair's balance. Route DRIP edits through the
        // reinvestment editor (defense-in-depth for the raw API; the frontend already routes there).
        if (transaction.RelatedTransactionId is not null)
            throw new BusinessRuleException(
                "This trade is part of a dividend reinvestment; edit it via the reinvestment editor.");

        // Side is immutable mid-edit: switching Buy<->Sell would invert both legs' signs and is
        // semantically a different transaction. The editor disables the toggle in edit mode.
        var existingSide = FromNormalShape(transaction.NormalShape);
        if (request.Side != existingSide)
            throw new BusinessRuleException(
                "A trade's side cannot be changed. Delete and recreate to switch between Buy and Sell.");

        var account = await LoadInvestmentAccountAsync(request.AccountId, ct);
        var (security, funding) = await ResolveCommoditiesAsync(request.SecurityId, request.FundingCurrencyId, ct);
        ValidateInputs(request.Quantity, request.UnitPrice);

        var touchedBefore = TouchedFor(transaction).ToList();

        audit.Track<AccountTransactionSnapshot, AccountTransaction>(
            transaction, "AccountTransaction", transaction.Id);

        transaction.OccurredAt = request.OccurredAt;
        transaction.Memo = NormalizeMemo(request.Memo);

        // Identify the existing slots by annotation (the security leg is the priced one) so split
        // Ids are preserved across the edit.
        var splits = transaction.Splits.ToList();
        var secSlot = splits.Single(s => s.UnitPriceRate is not null);
        var cashSlot = splits.Single(s => s.UnitPriceRate is null);

        var sign = SignFor(existingSide);
        var total = request.Quantity * request.UnitPrice;

        cashSlot.AccountId = account.Id;
        cashSlot.CommodityId = funding.Id;
        cashSlot.Quantity = sign * total;
        cashSlot.UnitPriceRate = null;
        cashSlot.UnitPriceCommodityId = null;

        secSlot.AccountId = account.Id;
        secSlot.CommodityId = security.Id;
        secSlot.Quantity = -sign * request.Quantity;
        secSlot.UnitPriceRate = request.UnitPrice;
        secSlot.UnitPriceCommodityId = funding.Id;

        await validator.ValidateAsync(transaction, ct);

        await linkedPrices.SyncForTransactionAsync(transaction, ct);

        var touched = touchedBefore.Concat(TouchedFor(transaction)).ToArray();
        return new TradeMutationResult(transaction, touched);
    }

    private async Task<NormalTransaction> LoadTradeAsync(Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.AccountTransactions
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw new EntityNotFoundException("Trade transaction not found.");

        if (transaction is not NormalTransaction normal
            || normal.NormalShape is not (NormalShape.InvestmentBuy or NormalShape.InvestmentSell))
            throw new BusinessRuleException("Transaction is not a trade.");

        // Defensive: the canonical writer always produces exactly two splits with exactly one priced
        // (security) leg. Guard so a structurally-broken row yields a clean 400 instead of an
        // InvalidOperationException (500) from the slot-identification Singles in UpdateAsync.
        if (normal.Splits.Count != 2 || normal.Splits.Count(s => s.UnitPriceRate is not null) != 1)
            throw new BusinessRuleException("Trade transaction is malformed.");

        return normal;
    }

    private async Task<Account> LoadInvestmentAccountAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.FindAsync([accountId], ct)
            ?? throw new EntityNotFoundException("Account not found.");

        AccountWritability.EnsureUserWritable(account);

        if (account.Kind != AccountKind.Investment)
            throw new BusinessRuleException("Trades can only be recorded on investment accounts.");

        return account;
    }

    private async Task<(Commodity security, Commodity funding)> ResolveCommoditiesAsync(
        Guid securityId, Guid fundingId, CancellationToken ct)
    {
        if (securityId == fundingId)
            throw new BusinessRuleException("A trade's security and funding currency must differ.");

        var ids = new[] { securityId, fundingId };
        var commodities = await db.Commodities.Where(c => ids.Contains(c.Id)).ToListAsync(ct);

        var security = commodities.FirstOrDefault(c => c.Id == securityId)
            ?? throw new EntityNotFoundException("Security commodity not found.");
        var funding = commodities.FirstOrDefault(c => c.Id == fundingId)
            ?? throw new EntityNotFoundException("Funding currency not found.");

        if (security.Kind != CommodityKind.Security)
            throw new BusinessRuleException("The traded commodity must be a security (Kind=Security).");
        if (funding.Kind != CommodityKind.Currency)
            throw new BusinessRuleException("The funding commodity must be a currency (Kind=Currency).");

        return (security, funding);
    }

    private static void ValidateInputs(decimal quantity, decimal unitPrice)
    {
        if (quantity <= 0m)
            throw new BusinessRuleException("Trade quantity must be greater than zero.");
        if (unitPrice <= 0m)
            throw new BusinessRuleException("Trade unit price must be greater than zero.");
    }

    private static (AccountTransactionSplit cash, AccountTransactionSplit security) BuildSplits(
        Guid accountId, Guid securityId, Guid fundingId,
        TradeSide side, decimal quantity, decimal unitPrice)
    {
        // Buy: cash leaves the account (-), units arrive (+). Sell: mirror. Both legs land on the
        // same investment account. The security leg carries the per-unit price annotation so the
        // transaction balances per commodity in the funding bucket: sign*total + (-sign*qty)*price = 0.
        var sign = SignFor(side);
        var total = quantity * unitPrice;

        var cash = new AccountTransactionSplit
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            CommodityId = fundingId,
            Quantity = sign * total,
        };

        var security = new AccountTransactionSplit
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            CommodityId = securityId,
            Quantity = -sign * quantity,
            UnitPriceRate = unitPrice,
            UnitPriceCommodityId = fundingId,
        };

        return (cash, security);
    }

    // Buy spends cash (-1); Sell receives cash (+1).
    private static decimal SignFor(TradeSide side) => side == TradeSide.Buy ? -1m : 1m;

    private static NormalShape ToNormalShape(TradeSide side) =>
        side == TradeSide.Buy ? NormalShape.InvestmentBuy : NormalShape.InvestmentSell;

    private static TradeSide FromNormalShape(NormalShape shape) =>
        shape == NormalShape.InvestmentBuy ? TradeSide.Buy : TradeSide.Sell;
}
