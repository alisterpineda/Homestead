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

/// <summary>One curated line to materialize as an Expense/Income flow, in the account's reporting currency.</summary>
public record CommitLineItem(FlowDirection Direction, decimal Amount, DateOnly Date, string? Description);

public record CommitStatementImportResponse
{
    /// <summary>True when a balance entry was created; false when the reviewer declined (Accepted=false).</summary>
    public required bool Committed { get; init; }
    public Guid? EntryId { get; init; }

    /// <summary>Number of Expense/Income flows created from the curated line list.</summary>
    public required int FlowsCreated { get; init; }

    public required Guid AccountId { get; init; }
}
