namespace Homestead.WebApi.Modules.Finances.Processing.Statements;

/// <summary>
/// Intermediate Representation of a parsed statement — a faithful transcription of what the document says,
/// with no finance judgment applied (that happens in <see cref="StatementMappingBuilder"/>). Phase 1
/// populates the header only; <see cref="LineItems"/> / <see cref="Holdings"/> are forward-compat for
/// Phase 2 and always null here.
/// </summary>
public record StatementIr
{
    public StatementKind Kind { get; init; }
    public required StatementHeaderIr Header { get; init; }

    // Phase 2 forward-compat — itemized ledger lines and brokerage holdings. Always null in Phase 1;
    // the fields exist so the staged JSON shape is stable across phases.
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

// Phase 2 forward-compat placeholders — not populated in Phase 1.
public record StatementLineItemIr(DateOnly? Date, string? Description, decimal? Amount);

public record StatementHoldingIr(string? Symbol, decimal? Quantity, MoneyIr? MarketValue);
