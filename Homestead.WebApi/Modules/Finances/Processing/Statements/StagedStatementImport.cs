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
}
