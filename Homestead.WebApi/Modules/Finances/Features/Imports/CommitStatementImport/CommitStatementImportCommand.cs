using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.CommitStatementImport;

public record CommitStatementImportCommand : ICommand<CommitStatementImportResponse>
{
    public Guid JobId { get; init; }
    public required decimal AssertedAmount { get; init; }
    public required DateOnly Date { get; init; }
    public string? Memo { get; init; }
    public required bool Accepted { get; init; }
}

public record CommitStatementImportResponse
{
    /// <summary>True when a balance entry was created; false when the reviewer declined (Accepted=false).</summary>
    public required bool Committed { get; init; }
    public Guid? EntryId { get; init; }
    public required Guid AccountId { get; init; }
}
