using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using static Homestead.WebApi.Modules.Finances.Services.TransactionWriteHelpers;

namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// Writes DRIP pairs — essentially <see cref="DividendService"/> + <see cref="TradeService"/> (Buy)
/// fused, with the two halves linked via <see cref="AccountTransaction.RelatedTransactionId"/>. DRIP
/// is the first and only producer of a non-null RelatedTransactionId.
/// </summary>
public class DripService(
    AppDbContext db,
    ISystemAccountProvider systemAccounts,
    ITransactionValidator validator,
    ILinkedPriceManager linkedPrices,
    IAuditContext audit) : IDripService
{
    private const string DefaultDividendDescription = "Dividend";

    public async Task<DripMutationResult> CreateAsync(CreateDripRequest request, CancellationToken ct)
    {
        var account = await LoadInvestmentAccountAsync(request.AccountId, ct);
        var (security, payment) = await ResolveCommoditiesAsync(request.SecurityId, request.PaymentCurrencyId, ct);
        ValidateInputs(request.Amount, request.Units);

        // System counterpart — do not writability-check it (mirrors DividendService).
        var income = await systemAccounts.GetIncomeAsync(ct);

        // The per-unit reinvestment price is derived: the user never types it.
        var unitPrice = request.Amount / request.Units;

        // The reinvested cash is `Units * unitPrice`, NOT the raw Amount. Computing it as the product
        // (exactly as TradeService does with quantity*unitPrice) is what makes the Buy half balance per
        // commodity AND nets cash to exactly zero across both halves: the weight-balance validator
        // recomputes the security leg's contribution as Quantity*Rate = Units*unitPrice, so the cash
        // leg must equal that same product. For non-terminating Amount/Units (e.g. $5 → 0.0523 units)
        // `Units * (Amount/Units)` differs from Amount by up to one decimal ULP, so using Amount for
        // the cash leg would fail the validator's strict `sum != 0` check; deriving `total` keeps both
        // halves exact. `total` equals Amount whenever the division terminates (the common case).
        var total = request.Units * unitPrice;

        var dividend = BuildDividendHalf(account.Id, income.Id, payment.Id, total, request.OccurredAt,
            DividendMemo(request.Description));
        var buy = BuildBuyHalf(account.Id, security.Id, payment.Id, request.Units, unitPrice, total,
            request.OccurredAt, BuyMemo(request.Description, security.Symbol));

        // The validator is per-transaction and generic (weight-balance + annotation-kind), so each half
        // passes independently. There is no multi-transaction validator; call it once per half.
        await validator.ValidateAsync(dividend, ct);
        await validator.ValidateAsync(buy, ct);

        db.AccountTransactions.Add(dividend);
        db.AccountTransactions.Add(buy);
        audit.Created("AccountTransaction", dividend.Id);
        audit.Created("AccountTransaction", buy.Id);

        // Insert both rows BEFORE linking them. The two halves reference each other via the nullable
        // self-FK; EF Core does NOT break that 2-cycle in one SaveChanges (it throws a circular
        // dependency), so flush the inserts first. This single save is the service's one unavoidable
        // deviation from the "let the handler's UnitOfWork own the flush" convention; it binds to the
        // command transaction via CommandSaveChangesInterceptor, staying atomic with the request.
        await db.SaveChangesAsync(ct);

        // Both rows now exist, so the reciprocal links are plain UPDATEs and the Buy half's price row is
        // a plain INSERT against an existing row — no insert ordering, no cycle. Leave them for the
        // caller's IUnitOfWork flush, exactly like the sibling shape services do for all their writes.
        dividend.RelatedTransactionId = buy.Id;
        buy.RelatedTransactionId = dividend.Id;

        // Only the Buy half is annotated — it emits the single CommodityPrice row. The Dividend half
        // has no price row, exactly like a standalone dividend.
        await linkedPrices.SyncForTransactionAsync(buy, ct);

        var touched = TouchedFor(dividend).Concat(TouchedFor(buy)).ToArray();
        return new DripMutationResult(dividend, buy, touched);
    }

    public async Task<DripMutationResult> UpdateAsync(
        Guid transactionId, UpdateDripRequest request, CancellationToken ct)
    {
        var (dividend, buy) = await LoadDripPairAsync(transactionId, ct);
        var income = await systemAccounts.GetIncomeAsync(ct);

        // Slot identification (mirrors the siblings): dividend by account-vs-Income identity, buy by
        // the annotation (the security leg is the priced one). Guarded in LoadDripPairAsync.
        if (dividend.Splits.Count(s => s.AccountId == income.Id) != 1)
            throw new BusinessRuleException("Transaction is not a DRIP pair.");
        var dividendAccountSlot = dividend.Splits.Single(s => s.AccountId != income.Id);
        var dividendIncomeSlot = dividend.Splits.Single(s => s.AccountId == income.Id);
        var buySecSlot = buy.Splits.Single(s => s.UnitPriceRate is not null);
        var buyCashSlot = buy.Splits.Single(s => s.UnitPriceRate is null);

        // Identity is immutable (K4): the account, security, and payment currency cannot change.
        var existingAccountId = buySecSlot.AccountId;
        var existingSecurityId = buySecSlot.CommodityId;
        var existingPaymentId = buyCashSlot.CommodityId;
        if (request.AccountId != existingAccountId
            || request.SecurityId != existingSecurityId
            || request.PaymentCurrencyId != existingPaymentId)
            throw new BusinessRuleException(
                "A DRIP's account, security, and payment currency can't be changed. "
                + "Delete and recreate it to change them.");

        // Defensive re-load: the account must still be user-writable and an investment account.
        var account = await LoadInvestmentAccountAsync(existingAccountId, ct);
        ValidateInputs(request.Amount, request.Units);

        var unitPrice = request.Amount / request.Units;
        var total = request.Units * unitPrice;

        var touchedBefore = TouchedFor(dividend).Concat(TouchedFor(buy)).ToList();

        audit.Track<AccountTransactionSnapshot, AccountTransaction>(dividend, "AccountTransaction", dividend.Id);
        audit.Track<AccountTransactionSnapshot, AccountTransaction>(buy, "AccountTransaction", buy.Id);

        dividend.OccurredAt = request.OccurredAt;
        buy.OccurredAt = request.OccurredAt;
        // The DRIP editor seeds its single description field from the Buy half's memo, which for a
        // blank-description DRIP is the auto-generated "Reinvest <symbol> dividend". Treat an incoming
        // description equal to that auto-default as blank, so a re-save regenerates the asymmetric
        // per-half defaults (the Dividend half stays "Dividend") instead of promoting the Buy phrasing
        // onto the Dividend half on an otherwise no-op edit. The symbol is needed both for this check
        // and for BuyMemo's own blank-default, so it is always looked up here.
        var buySymbol = await SecuritySymbolAsync(existingSecurityId, ct);
        var description = request.Description;
        if (!string.IsNullOrWhiteSpace(description) && description.Trim() == BuyMemo(null, buySymbol))
            description = null;
        dividend.Memo = DividendMemo(description);
        buy.Memo = BuyMemo(description, buySymbol);

        // Rewrite both halves' splits in place, preserving split Ids.
        dividendAccountSlot.CommodityId = existingPaymentId;
        dividendAccountSlot.Quantity = total;
        dividendIncomeSlot.CommodityId = existingPaymentId;
        dividendIncomeSlot.Quantity = -total;

        buyCashSlot.AccountId = account.Id;
        buyCashSlot.CommodityId = existingPaymentId;
        buyCashSlot.Quantity = -total;
        buyCashSlot.UnitPriceRate = null;
        buyCashSlot.UnitPriceCommodityId = null;

        buySecSlot.AccountId = account.Id;
        buySecSlot.CommodityId = existingSecurityId;
        buySecSlot.Quantity = request.Units;
        buySecSlot.UnitPriceRate = unitPrice;
        buySecSlot.UnitPriceCommodityId = existingPaymentId;

        await validator.ValidateAsync(dividend, ct);
        await validator.ValidateAsync(buy, ct);

        await linkedPrices.SyncForTransactionAsync(buy, ct);

        var touched = touchedBefore
            .Concat(TouchedFor(dividend))
            .Concat(TouchedFor(buy))
            .ToArray();
        return new DripMutationResult(dividend, buy, touched);
    }

    public async Task<DripMutationResult> DeleteBothAsync(Guid transactionId, CancellationToken ct)
    {
        var (dividend, buy) = await LoadDripPairAsync(transactionId, ct);

        var touched = TouchedFor(dividend).Concat(TouchedFor(buy)).ToArray();

        // Break the mutual FK and flush BEFORE removing — two rows referencing each other form a
        // delete cycle EF can't order (the same limitation as the create insert cycle). Once the links
        // are null, the two row deletes have no FK dependency on each other.
        dividend.RelatedTransactionId = null;
        buy.RelatedTransactionId = null;
        await db.SaveChangesAsync(ct);

        // Audit the cascading price rows BEFORE deletion (no-op for the dividend half; one row for the
        // buy half). The rows themselves cascade-delete via the CreatedByTransactionId FK.
        await linkedPrices.AuditCascadeDeletesAsync(dividend.Id, ct);
        await linkedPrices.AuditCascadeDeletesAsync(buy.Id, ct);

        db.AccountTransactions.Remove(dividend);
        db.AccountTransactions.Remove(buy);
        audit.Deleted("AccountTransaction", dividend.Id, dividend.Memo);
        audit.Deleted("AccountTransaction", buy.Id, buy.Memo);

        return new DripMutationResult(null, null, touched);
    }

    private async Task<(NormalTransaction dividend, NormalTransaction buy)> LoadDripPairAsync(
        Guid transactionId, CancellationToken ct)
    {
        var clicked = await db.AccountTransactions
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw new EntityNotFoundException("Transaction not found.");

        if (clicked is not NormalTransaction clickedNormal || clickedNormal.RelatedTransactionId is null)
            throw new BusinessRuleException("Transaction is not a DRIP pair.");

        var partner = await db.AccountTransactions
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.Id == clickedNormal.RelatedTransactionId, ct)
            ?? throw new BusinessRuleException("Transaction is not a DRIP pair.");

        if (partner is not NormalTransaction partnerNormal)
            throw new BusinessRuleException("Transaction is not a DRIP pair.");

        // Reciprocal links must match.
        if (partnerNormal.RelatedTransactionId != clickedNormal.Id)
            throw new BusinessRuleException("Transaction is not a DRIP pair.");

        // Exactly one Dividend half and one InvestmentBuy half (counting, not Single, so a degenerate
        // same-shape pair yields a clean 400 rather than an InvalidOperationException 500).
        var halves = new[] { clickedNormal, partnerNormal };
        var dividends = halves.Where(t => t.NormalShape == NormalShape.Dividend).ToList();
        var buys = halves.Where(t => t.NormalShape == NormalShape.InvestmentBuy).ToList();
        if (dividends.Count != 1 || buys.Count != 1)
            throw new BusinessRuleException("Transaction is not a DRIP pair.");
        var dividend = dividends[0];
        var buy = buys[0];

        // Defensive: each half is the canonical 2-split shape with exactly one priced (buy) leg.
        if (dividend.Splits.Count != 2
            || buy.Splits.Count != 2
            || buy.Splits.Count(s => s.UnitPriceRate is not null) != 1)
            throw new BusinessRuleException("DRIP pair is malformed.");

        return (dividend, buy);
    }

    private async Task<Account> LoadInvestmentAccountAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.FindAsync([accountId], ct)
            ?? throw new EntityNotFoundException("Account not found.");

        AccountWritability.EnsureUserWritable(account);

        if (account.Kind != AccountKind.Investment)
            throw new BusinessRuleException("Dividend reinvestments can only be recorded on investment accounts.");

        return account;
    }

    private async Task<(Commodity security, Commodity payment)> ResolveCommoditiesAsync(
        Guid securityId, Guid paymentId, CancellationToken ct)
    {
        if (securityId == paymentId)
            throw new BusinessRuleException("A DRIP's security and payment currency must differ.");

        var ids = new[] { securityId, paymentId };
        var commodities = await db.Commodities.Where(c => ids.Contains(c.Id)).ToListAsync(ct);

        var security = commodities.FirstOrDefault(c => c.Id == securityId)
            ?? throw new EntityNotFoundException("Security commodity not found.");
        var payment = commodities.FirstOrDefault(c => c.Id == paymentId)
            ?? throw new EntityNotFoundException("Payment currency not found.");

        if (security.Kind != CommodityKind.Security)
            throw new BusinessRuleException("The reinvested commodity must be a security (Kind=Security).");
        if (payment.Kind != CommodityKind.Currency)
            throw new BusinessRuleException("A DRIP's payment commodity must be a currency (Kind=Currency).");

        return (security, payment);
    }

    private async Task<string> SecuritySymbolAsync(Guid securityId, CancellationToken ct) =>
        await db.Commodities.Where(c => c.Id == securityId).Select(c => c.Symbol).FirstOrDefaultAsync(ct)
            ?? "";

    private static NormalTransaction BuildDividendHalf(
        Guid accountId, Guid incomeId, Guid paymentId, decimal total,
        DateTimeOffset occurredAt, string memo) =>
        new()
        {
            OccurredAt = occurredAt,
            Memo = memo,
            Type = TransactionType.Normal,
            NormalShape = NormalShape.Dividend,
            Splits =
            [
                new AccountTransactionSplit
                {
                    AccountId = accountId,
                    CommodityId = paymentId,
                    Quantity = total,
                },
                new AccountTransactionSplit
                {
                    AccountId = incomeId,
                    CommodityId = paymentId,
                    Quantity = -total,
                },
            ],
        };

    private static NormalTransaction BuildBuyHalf(
        Guid accountId, Guid securityId, Guid paymentId, decimal units, decimal unitPrice, decimal total,
        DateTimeOffset occurredAt, string memo) =>
        new()
        {
            OccurredAt = occurredAt,
            Memo = memo,
            Type = TransactionType.Normal,
            NormalShape = NormalShape.InvestmentBuy,
            Splits =
            [
                // Cash leaves the account (= the reinvested dividend), so it nets against the Dividend
                // half's inflow to leave the account's cash unchanged.
                new AccountTransactionSplit
                {
                    AccountId = accountId,
                    CommodityId = paymentId,
                    Quantity = -total,
                },
                // Units arrive, annotated with the derived per-unit price. This is the only annotated
                // leg in the pair, so it emits the single linked CommodityPrice row.
                new AccountTransactionSplit
                {
                    AccountId = accountId,
                    CommodityId = securityId,
                    Quantity = units,
                    UnitPriceRate = unitPrice,
                    UnitPriceCommodityId = paymentId,
                },
            ],
        };

    private static void ValidateInputs(decimal amount, decimal units)
    {
        if (amount <= 0m)
            throw new BusinessRuleException("Dividend amount must be greater than zero.");
        if (units <= 0m)
            throw new BusinessRuleException("Reinvested units must be greater than zero.");
    }

    private static string DividendMemo(string? description) =>
        string.IsNullOrWhiteSpace(description) ? DefaultDividendDescription : description.Trim();

    private static string BuyMemo(string? description, string securitySymbol) =>
        string.IsNullOrWhiteSpace(description)
            ? $"Reinvest {securitySymbol} dividend"
            : description.Trim();
}
