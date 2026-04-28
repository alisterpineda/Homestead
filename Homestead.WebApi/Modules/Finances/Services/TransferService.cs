using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public class TransferService(
    AppDbContext db,
    ITransactionValidator validator,
    ILinkedPriceManager linkedPrices,
    IAuditContext audit) : ITransferService
{
    private const string DefaultDescription = "Transfer";

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
            Id = Guid.NewGuid(),
            OccurredAt = occurredAt,
            Description = NormalizeDescription(request.Description),
            Type = TransactionType.Normal,
            Splits = [sourceSplit, destSplit],
        };

        validator.Validate(transaction);

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
        transaction.Description = NormalizeDescription(request.Description);

        var existing = transaction.Splits.ToList();
        var sourceSlot = existing[0];
        var destSlot = existing[1];

        PopulateSplit(sourceSlot, source.Id, sourceCurrency.Id, -request.SourceAmount);
        PopulateSplit(destSlot, destination.Id, destinationCurrency.Id,
            request.DestinationAmount ?? request.SourceAmount);

        if (!AreSameCurrency(sourceCurrency, destinationCurrency))
        {
            sourceSlot.UnitPriceRate = DeriveRate(request.SourceAmount, request.DestinationAmount!.Value);
            sourceSlot.UnitPriceCommodityId = destinationCurrency.Id;
            destSlot.UnitPriceRate = null;
            destSlot.UnitPriceCommodityId = null;
        }
        else
        {
            sourceSlot.UnitPriceRate = null;
            sourceSlot.UnitPriceCommodityId = null;
            destSlot.UnitPriceRate = null;
            destSlot.UnitPriceCommodityId = null;
        }

        validator.Validate(transaction);

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
        audit.Deleted("AccountTransaction", transaction.Id, transaction.Description);

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
            Id = Guid.NewGuid(),
            AccountId = sourceAccountId,
            CommodityId = sourceCurrency.Id,
            Quantity = -sourceAmount,
        };

        var destSplit = new AccountTransactionSplit
        {
            Id = Guid.NewGuid(),
            AccountId = destinationAccountId,
            CommodityId = destinationCurrency.Id,
            Quantity = destinationAmount ?? sourceAmount,
        };

        if (!AreSameCurrency(sourceCurrency, destinationCurrency))
        {
            sourceSplit.UnitPriceRate = DeriveRate(sourceAmount, destinationAmount!.Value);
            sourceSplit.UnitPriceCommodityId = destinationCurrency.Id;
        }

        return (sourceSplit, destSplit);
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

    private static string NormalizeDescription(string? description) =>
        TransactionDescriptions.NormalizeOrDefault(description, DefaultDescription);

    private static IEnumerable<TouchedPair> TouchedFor(AccountTransaction transaction)
    {
        foreach (var split in transaction.Splits)
            yield return new TouchedPair(split.AccountId, split.CommodityId, transaction.OccurredAt);
    }
}
