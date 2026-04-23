using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.DeleteAccount;

public record DeleteAccountCommand(Guid Id) : IRequest<bool>;
