namespace Homestead.WebApi.Modules.Finances.Processing.Statements;

/// <summary>
/// Intermediate Representation of a parsed statement — a faithful transcription of what the document says,
/// with no finance judgment applied (that happens in <see cref="StatementMappingBuilder"/>). The header is
/// always the primary signal (Feature A closing balance). <see cref="LineItems"/> is populated from Phase 2
/// (Feature B) onward; <see cref="Holdings"/> stays forward-compat and null (brokerage holdings deferred).
/// </summary>
public record StatementIr
{
    public StatementKind Kind { get; init; }
    public required StatementHeaderIr Header { get; init; }

    // Itemized ledger lines (Feature B). Brokerage holdings (<see cref="Holdings"/>) remain forward-compat
    // and null; the field exists so the staged JSON shape is stable across phases.
    public IReadOnlyList<StatementLineItemIr>? LineItems { get; init; }
    public IReadOnlyList<StatementHoldingIr>? Holdings { get; init; }
}

/// <summary>
/// The statement's summary block: institution, masked account hint, currency, period, and the opening /
/// closing balances. Every field is nullable — the extractor reports only what the document actually shows.
/// </summary>
public record StatementHeaderIr
{
    public string? Institution { get; init; }

    /// <summary>The account/card number as shown on the statement (usually already masked, e.g. ••••1234).</summary>
    public string? AccountNumberMasked { get; init; }

    /// <summary>ISO 4217 currency code the statement is denominated in, if stated.</summary>
    public string? Currency { get; init; }

    public DateOnly? PeriodStart { get; init; }
    public DateOnly? PeriodEnd { get; init; }

    public MoneyIr? OpeningBalance { get; init; }
    public MoneyIr? ClosingBalance { get; init; }
}

/// <summary>
/// A monetary value transcribed from the statement. <see cref="SourceAnchor"/> echoes the source snippet
/// the value was read from (balance values only) so a reviewer can locate it — there are no numeric
/// confidence scores.
/// </summary>
public record MoneyIr(decimal Amount, string? SourceAnchor);

public enum StatementKind
{
    Bank,
    CreditCard,
    Brokerage,
    Unknown,
}

/// <summary>
/// Direction of a transcribed statement line, from the account-holder's spending perspective so it maps
/// cleanly onto an Expense/Income flow. <see cref="Out"/> = money spent / charge / debit / withdrawal;
/// <see cref="In"/> = money received / payment / credit / refund / deposit.
/// </summary>
public enum StatementLineDirection { Out, In }

/// <summary>
/// One transcribed transaction row (Feature B). Every field is nullable — the extractor reports only what
/// the row actually shows; a row missing <see cref="Amount"/> or <see cref="Direction"/> is still surfaced
/// (flagged un-mappable) rather than dropped.
/// </summary>
public record StatementLineItemIr(
    DateOnly? Date,
    string? Description,
    decimal? Amount,                    // positive magnitude, never signed
    StatementLineDirection? Direction,
    decimal? RunningBalance,            // only when the statement prints a running balance
    Guid? CategoryHint);               // forward-compat category suggestion; ALWAYS null this cut

// Forward-compat placeholder — not populated (brokerage holdings deferred).
public record StatementHoldingIr(string? Symbol, decimal? Quantity, MoneyIr? MarketValue);
