using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using static Homestead.WebApi.Modules.Finances.Services.TransactionWriteHelpers;

namespace Homestead.WebApi.Modules.Finances.Services;

public class TransferService(
    AppDbContext db,
    ITransactionValidator validator,
    ILinkedPriceManager linkedPrices,
    IAuditContext audit) : ITransferService
{
    public async Task<TransferMutationResult> CreateAsync(
        CreateTransferRequest request, CancellationToken ct)
    {
        var (source, destination) = await LoadAccountsAsync(
            request.SourceAccountId, request.DestinationAccountId, ct);
        var (sourceCurrency, destinationCurrency) = await ResolveCurrenciesAsync(source, destination, ct);

        ValidateInputs(request.SourceAmount, request.DestinationAmount, sourceCurrency, destinationCurrency);

        var occurredAt = ResolveOccurredAt(request.Date);

        var (sourceSplit, destSplit) = BuildSplits(
            source.Id, destination.Id, sourceCurrency, destinationCurrency,
            request.SourceAmount, request.DestinationAmount);

        var transaction = new NormalTransaction
        {
            OccurredAt = occurredAt,
            Memo = NormalizeMemo(request.Memo),
            Type = TransactionType.Normal,
            NormalShape = NormalShape.Transfer,
            Splits = [sourceSplit, destSplit],
        };

        await validator.ValidateAsync(transaction, ct);

        db.AccountTransactions.Add(transaction);
        audit.Created("AccountTransaction", transaction.Id);

        await linkedPrices.SyncForTransactionAsync(transaction, ct);

        var touched = TouchedFor(transaction).ToArray();
        return new TransferMutationResult(transaction, touched);
    }

    public async Task<TransferMutationResult> UpdateAsync(
        Guid transactionId, UpdateTransferRequest request, CancellationToken ct)
    {
        var transaction = await LoadTransferAsync(transactionId, ct);

        // Shape is immutable on edit, and only reads/deletes are shape-agnostic: the transfer editor
        // must refuse a trade or dividend (also 2-split normals) so it can't rewrite their splits into
        // transfer legs while leaving NormalShape intact. (Delete deliberately stays shape-agnostic and
        // reuses LoadTransferAsync, so the guard lives here, not in the loader.)
        if (transaction.NormalShape != NormalShape.Transfer)
            throw new BusinessRuleException("Transaction is not a transfer.");

        var (source, destination) = await LoadAccountsAsync(
            request.SourceAccountId, request.DestinationAccountId, ct);
        var (sourceCurrency, destinationCurrency) = await ResolveCurrenciesAsync(source, destination, ct);

        ValidateInputs(request.SourceAmount, request.DestinationAmount, sourceCurrency, destinationCurrency);

        // Pairs touched in the BEFORE state (so vacated chains get re-reconciled).
        var touchedBefore = TouchedFor(transaction).ToList();

        audit.Track<AccountTransactionSnapshot, AccountTransaction>(
            transaction, "AccountTransaction", transaction.Id);

        var newOccurredAt = ResolveOccurredAt(request.Date);
        transaction.OccurredAt = newOccurredAt;
        transaction.Memo = NormalizeMemo(request.Memo);

        var existing = transaction.Splits.ToList();
        var sourceSlot = existing[0];
        var destSlot = existing[1];

        PopulateSplit(sourceSlot, source.Id, sourceCurrency.Id, -request.SourceAmount);
        PopulateSplit(destSlot, destination.Id, destinationCurrency.Id,
            request.DestinationAmount ?? request.SourceAmount);

        if (!AreSameCurrency(sourceCurrency, destinationCurrency))
        {
            ApplyCrossCurrencyBridge(sourceSlot, destSlot, request.SourceAmount,
                request.DestinationAmount!.Value, destinationCurrency.Id);
        }
        else
        {
            sourceSlot.UnitPriceRate = null;
            sourceSlot.UnitPriceCommodityId = null;
            destSlot.UnitPriceRate = null;
            destSlot.UnitPriceCommodityId = null;
        }

        await validator.ValidateAsync(transaction, ct);

        await linkedPrices.SyncForTransactionAsync(transaction, ct);

        var touched = touchedBefore.Concat(TouchedFor(transaction)).ToArray();
        return new TransferMutationResult(transaction, touched);
    }

    public async Task<TransferMutationResult> DeleteAsync(Guid transactionId, CancellationToken ct)
    {
        var transaction = await LoadTransferAsync(transactionId, ct);

        var touched = TouchedFor(transaction).ToArray();

        await linkedPrices.AuditCascadeDeletesAsync(transactionId, ct);

        db.AccountTransactions.Remove(transaction);
        audit.Deleted("AccountTransaction", transaction.Id, transaction.Memo);

        return new TransferMutationResult(null, touched);
    }

    private async Task<NormalTransaction> LoadTransferAsync(Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.AccountTransactions
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw new EntityNotFoundException("Transfer transaction not found.");

        if (transaction is not NormalTransaction normal || normal.Splits.Count != 2)
            throw new BusinessRuleException("Transaction is not a transfer.");

        return normal;
    }

    private async Task<(Account source, Account destination)> LoadAccountsAsync(
        Guid sourceId, Guid destId, CancellationToken ct)
    {
        if (sourceId == destId)
            throw new BusinessRuleException("Transfer source and destination must differ.");

        var accounts = await db.Accounts
            .Where(a => a.Id == sourceId || a.Id == destId)
            .ToListAsync(ct);

        var source = accounts.FirstOrDefault(a => a.Id == sourceId)
            ?? throw new EntityNotFoundException("Source account not found.");
        var destination = accounts.FirstOrDefault(a => a.Id == destId)
            ?? throw new EntityNotFoundException("Destination account not found.");

        AccountWritability.EnsureUserWritable(source);
        AccountWritability.EnsureUserWritable(destination);

        return (source, destination);
    }

    private async Task<(Commodity source, Commodity destination)> ResolveCurrenciesAsync(
        Account source, Account destination, CancellationToken ct)
    {
        var ids = new[] { source.ReportingCurrencyId, destination.ReportingCurrencyId }.Distinct().ToArray();
        var commodities = await db.Commodities.Where(c => ids.Contains(c.Id)).ToListAsync(ct);

        var sourceCurrency = commodities.First(c => c.Id == source.ReportingCurrencyId);
        var destinationCurrency = commodities.First(c => c.Id == destination.ReportingCurrencyId);

        if (sourceCurrency.Kind != CommodityKind.Currency)
            throw new BusinessRuleException("Source account's reporting commodity must be a currency.");
        if (destinationCurrency.Kind != CommodityKind.Currency)
            throw new BusinessRuleException("Destination account's reporting commodity must be a currency.");

        return (sourceCurrency, destinationCurrency);
    }

    private static void ValidateInputs(decimal sourceAmount, decimal? destinationAmount,
        Commodity sourceCurrency, Commodity destinationCurrency)
    {
        if (sourceAmount <= 0m)
            throw new BusinessRuleException("Transfer source amount must be greater than zero.");

        var sameCurrency = AreSameCurrency(sourceCurrency, destinationCurrency);
        if (sameCurrency)
        {
            if (destinationAmount is decimal dest && dest != sourceAmount)
                throw new BusinessRuleException(
                    "Same-currency transfers must not specify a different destination amount.");
        }
        else
        {
            if (destinationAmount is not decimal dest)
                throw new BusinessRuleException(
                    "Cross-currency transfers must specify a destination amount.");
            if (dest <= 0m)
                throw new BusinessRuleException("Transfer destination amount must be greater than zero.");
        }
    }

    private static (AccountTransactionSplit source, AccountTransactionSplit dest) BuildSplits(
        Guid sourceAccountId, Guid destinationAccountId,
        Commodity sourceCurrency, Commodity destinationCurrency,
        decimal sourceAmount, decimal? destinationAmount)
    {
        // Beancount sign follows flow direction: source loses (stored -), destination gains (stored +).
        // For an asset, decrease/increase maps directly to negative/positive stored quantity. For a
        // liability, the stored balance is itself negative; an outflow makes it more negative (-X)
        // and an inflow brings it toward zero (+X), which is exactly what -X / +X give us. So no
        // sign flip per AccountType is needed for a transfer.
        var sourceSplit = new AccountTransactionSplit
        {
            AccountId = sourceAccountId,
            CommodityId = sourceCurrency.Id,
            Quantity = -sourceAmount,
        };

        var destSplit = new AccountTransactionSplit
        {
            AccountId = destinationAccountId,
            CommodityId = destinationCurrency.Id,
            Quantity = destinationAmount ?? sourceAmount,
        };

        if (!AreSameCurrency(sourceCurrency, destinationCurrency))
            ApplyCrossCurrencyBridge(sourceSplit, destSplit, sourceAmount, destinationAmount!.Value,
                destinationCurrency.Id);

        return (sourceSplit, destSplit);
    }

    // Bridges a cross-currency transfer's two legs. The source leg carries the price annotation
    // (rate quoted in the destination currency); the destination leg's quantity is derived from the
    // SAME product the validator recomputes for the source weight (Quantity * rate). Storing the raw
    // typed destinationAmount instead would leave a ~1e-28 residue for non-terminating rates,
    // tripping the zero-tolerance per-commodity balance check (400). Mirrors TradeService/DripService;
    // the stored destination quantity is off the typed amount only far below any currency's display
    // precision. Do NOT pre-round the rate — that would push the destination further from the amount.
    private static void ApplyCrossCurrencyBridge(
        AccountTransactionSplit sourceSplit, AccountTransactionSplit destSplit,
        decimal sourceAmount, decimal destinationAmount, Guid destinationCurrencyId)
    {
        var rate = DeriveRate(sourceAmount, destinationAmount);
        sourceSplit.UnitPriceRate = rate;
        sourceSplit.UnitPriceCommodityId = destinationCurrencyId;
        destSplit.Quantity = sourceAmount * rate;
        destSplit.UnitPriceRate = null;
        destSplit.UnitPriceCommodityId = null;
    }

    private static void PopulateSplit(AccountTransactionSplit split, Guid accountId, Guid commodityId,
        decimal quantity)
    {
        split.AccountId = accountId;
        split.CommodityId = commodityId;
        split.Quantity = quantity;
    }

    private static bool AreSameCurrency(Commodity a, Commodity b) => a.Id == b.Id;

    private static decimal DeriveRate(decimal sourceAmount, decimal destinationAmount) =>
        destinationAmount / sourceAmount;

    private static DateTimeOffset ResolveOccurredAt(DateOnly date) => date.ToStartOfDayUtc();
}
