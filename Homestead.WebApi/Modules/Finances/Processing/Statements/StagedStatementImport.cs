namespace Homestead.WebApi.Modules.Finances.Processing.Statements;

/// <summary>
/// The parse job's staged output, serialized into <c>ProcessingJob.Result</c>. Nothing here has touched the
/// ledger — the review GET reads it back, and the commit endpoint turns the (possibly-edited) proposal into
/// a real <c>BalanceEntry</c>.
/// </summary>
public record StagedStatementImport
{
    public required Guid AccountId { get; init; }
    public required Guid DocumentId { get; init; }
    public required Guid DocumentVersionId { get; init; }
    public required StatementIr Ir { get; init; }
    public required StatementMappingProposal Proposal { get; init; }

    /// <summary>
    /// True when the statement text exceeded the extraction input budget and was clipped before the model
    /// saw it — a completeness safety-net so the reviewer is warned that some trailing lines may be missing.
    /// </summary>
    public bool TextTruncated { get; init; }
}
