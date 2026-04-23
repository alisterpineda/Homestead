using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccount;

public record GetAccountQuery(Guid Id) : IRequest<AccountResponse?>;
