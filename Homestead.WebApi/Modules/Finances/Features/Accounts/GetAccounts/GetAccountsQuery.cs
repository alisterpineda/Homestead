using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccounts;

public record GetAccountsQuery : PaginatedRequest
{
    public AccountKind? Kind { get; init; }
    public bool? IncludeArchived { get; init; }

    /// <summary>
    /// Cutoff for transaction inclusion and price resolution. Defaults to end-of-day
    /// UTC today when null. Frontend sends a local-end-of-day offset so user "today"
    /// is the source of truth.
    /// </summary>
    public DateTimeOffset? AsOf { get; init; }
}
