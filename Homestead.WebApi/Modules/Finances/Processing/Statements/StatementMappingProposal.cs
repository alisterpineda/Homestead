namespace Homestead.WebApi.Modules.Finances.Processing.Statements;

/// <summary>
/// Deterministic (Tier-1, no-AI) mapping of a <see cref="StatementIr"/> to a proposed finance change,
/// plus the guard/dedup signals a reviewer needs before committing.
/// </summary>
public record StatementMappingProposal
{
    /// <summary>The proposed closing-balance assertion, or null when the statement carried no closing balance.</summary>
    public ProposedBalanceEntry? BalanceEntry { get; init; }

    /// <summary>Whether the statement's masked account hint matches the target account's stored number.</summary>
    public AccountNumberMatch AccountNumberMatch { get; init; }

    /// <summary>
    /// True when a balance assertion already exists for this account + reporting currency on the proposed
    /// date. Computed at extraction time; re-checked against the live ledger at commit (it can go stale).
    /// </summary>
    public bool DuplicateAssertionExists { get; init; }
}

/// <summary>
/// A proposed <c>BalanceEntry</c> assertion for the account's reporting currency. The reviewer can edit the
/// amount/date/memo before committing.
/// </summary>
public record ProposedBalanceEntry
{
    /// <summary>Closing-balance amount as transcribed (magnitude reviewed by a human; the server applies account-type sign).</summary>
    public required decimal AssertedAmount { get; init; }

    /// <summary>The statement's period-end date, or null when the extractor couldn't determine it.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>Source snippet the closing balance was read from, for reviewer traceability.</summary>
    public string? SourceAnchor { get; init; }

    /// <summary>Pre-checked for the reviewer; defaults to accepted.</summary>
    public bool Accepted { get; init; } = true;
}

/// <summary>
/// Result of comparing the target account's stored number against the statement's masked hint.
/// <c>Unknown</c> when either side is absent or too short to compare.
/// </summary>
public enum AccountNumberMatch
{
    Match,
    Mismatch,
    Unknown,
}
