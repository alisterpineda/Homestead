using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccounts;

public record GetAccountsQuery : PaginatedRequest, IRequest<PaginatedResponse<AccountResponse>>
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
