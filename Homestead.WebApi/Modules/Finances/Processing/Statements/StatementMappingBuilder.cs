using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Processing.Statements;

/// <summary>
/// Turns a faithful <see cref="StatementIr"/> into a deterministic <see cref="StatementMappingProposal"/>
/// (no AI): the closing-balance assertion, the account-number mis-binding guard, and the same-date dedup
/// flag. The pure sub-steps (<see cref="ProposeBalanceEntry"/>, <see cref="MatchAccountNumber"/>) are static
/// and directly unit-testable; the dedup query needs the ledger, so the composed <see cref="BuildAsync"/> is
/// an instance method.
/// </summary>
public class StatementMappingBuilder(AppDbContext db)
{
    public async Task<StatementMappingProposal> BuildAsync(
        StatementIr ir, Account account, CancellationToken ct)
    {
        var proposal = ProposeBalanceEntry(ir);
        var match = MatchAccountNumber(account.AccountNumber, ir.Header.AccountNumberMasked);

        var duplicateExists = false;
        if (proposal is { Date: { } date })
        {
            // Dedup against an existing assertion for (account, reporting currency, period-end day). Balance
            // entries land at end-of-day UTC, so per-instant equality is per-day equality.
            var occurredAt = date.ToEndOfDayUtc();
            duplicateExists = await db.AccountTransactions
                .OfType<BalanceEntryTransaction>()
                .AsNoTracking()
                .AnyAsync(e => e.AccountId == account.Id
                            && e.AssertedCommodityId == account.ReportingCurrencyId
                            && e.OccurredAt == occurredAt, ct);
        }

        return new StatementMappingProposal
        {
            BalanceEntry = proposal,
            AccountNumberMatch = match,
            DuplicateAssertionExists = duplicateExists,
        };
    }

    /// <summary>
    /// Closing balance + period-end → one proposed balance-entry assertion. Null when the statement carried
    /// no closing balance (nothing to reconcile against in Phase 1).
    /// </summary>
    public static ProposedBalanceEntry? ProposeBalanceEntry(StatementIr ir)
    {
        var closing = ir.Header.ClosingBalance;
        if (closing is null)
            return null;

        return new ProposedBalanceEntry
        {
            AssertedAmount = closing.Amount,
            Date = ir.Header.PeriodEnd,
            SourceAnchor = closing.SourceAnchor,
            Accepted = true,
        };
    }

    /// <summary>
    /// Mis-binding guard: compares the target account's stored number against the statement's masked hint by
    /// their last-4 comparable suffix. The full number stays server-side — only the match verdict is exposed.
    /// </summary>
    public static AccountNumberMatch MatchAccountNumber(string? accountNumberFull, string? statementMaskedHint)
    {
        var stored = AccountNumberMasking.ComparableSuffix(accountNumberFull);
        var hint = AccountNumberMasking.ComparableSuffix(statementMaskedHint);

        if (stored is null || hint is null)
            return AccountNumberMatch.Unknown;

        return stored == hint ? AccountNumberMatch.Match : AccountNumberMatch.Mismatch;
    }
}
