using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.CommitStatementImport;

public record CommitStatementImportCommand : ICommand<CommitStatementImportResponse>
{
    public Guid JobId { get; init; }

    // ─── Feature A: the closing-balance assertion ───
    /// <summary>The reviewer's balance-entry decision — true records the assertion, false declines it.</summary>
    public required bool Accepted { get; init; }
    public required decimal AssertedAmount { get; init; }
    public required DateOnly Date { get; init; }
    public string? Memo { get; init; }

    // ─── Feature B: the curated itemized flows ───
    /// <summary>
    /// The final curated line list — accepted rows with any in-review edits already applied. The review page
    /// owns edit state; every row here is server-validated and provenance-stamped on commit. Empty when the
    /// reviewer accepted no lines (or the statement had none). Independent of <see cref="Accepted"/>: the
    /// reviewer can decline the balance entry yet still commit lines, and vice versa.
    /// </summary>
    public IReadOnlyList<CommitLineItem> LineItems { get; init; } = [];
}

/// <summary>
/// One curated line to materialize as an Expense/Income flow, in the account's reporting currency.
/// <para>
/// <see cref="Index"/> is the stable <c>ProposedFlow.Index</c> — it lets the server recover the *staged* raw
/// description (independent of the editable <see cref="Description"/> memo) as the learning key. It is
/// <c>null</c> for a **manual** line the reviewer added (no staged origin), which therefore has no raw text
/// to learn against and is never taught, regardless of <see cref="Learn"/>.
/// <see cref="Learn"/> is the explicit-signal flag: true only when the reviewer set this payee this session
/// (pick / create / accept / override). It is false for an untouched tier-① pre-fill and for the
/// warning-dialog auto-apply, both of which apply the payee but must *not* teach the learning table.
/// <see cref="CategoryId"/> is client-authoritative: the review page resolves each line's category
/// (pre-filling from the payee default when the setting is on, honouring overrides + explicit clears) and
/// sends it verbatim — <c>null</c> means Uncategorised. The server applies it directly and 400s on an
/// invalid (non-category / wrong-direction / archived) id; it no longer derives category from the payee.
/// </para>
/// </summary>
public record CommitLineItem(
    int? Index, FlowDirection Direction, decimal Amount, DateOnly Date,
    string? Description, Guid? PayeeId = null, bool Learn = false, Guid? CategoryId = null);

public record CommitStatementImportResponse
{
    /// <summary>True when a balance entry was created; false when the reviewer declined (Accepted=false).</summary>
    public required bool Committed { get; init; }
    public Guid? EntryId { get; init; }

    /// <summary>Number of Expense/Income flows created from the curated line list.</summary>
    public required int FlowsCreated { get; init; }

    public required Guid AccountId { get; init; }
}
