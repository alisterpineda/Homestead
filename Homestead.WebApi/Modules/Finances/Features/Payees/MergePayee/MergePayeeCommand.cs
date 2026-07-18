using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.MergePayee;

/// <summary>Request body for merge: the payee to keep.</summary>
public record MergePayeeRequest(Guid TargetId);

/// <summary>
/// Consolidate a source payee into a target: re-point every transaction and every learned mapping from
/// source to target, then delete the source. The cleanup valve for AI-created near-duplicates
/// ("Blue Bottle" / "Blue Bottle Coffee") that a case-insensitive name check can't catch.
/// </summary>
public record MergePayeeCommand : ICommand<PayeeResponse>
{
    public required Guid SourceId { get; init; }
    public required Guid TargetId { get; init; }
}
