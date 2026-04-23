using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccounts;

public record GetAccountsQuery : PaginatedRequest, IRequest<PaginatedResponse<AccountResponse>>
{
    public AccountType? AccountType { get; init; }
    public Guid? ParentId { get; init; }
    public bool? IncludeArchived { get; init; }
}
