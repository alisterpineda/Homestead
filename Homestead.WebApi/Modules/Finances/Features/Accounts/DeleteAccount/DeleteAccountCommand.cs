using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.DeleteAccount;

public record DeleteAccountCommand(Guid Id) : ICommand<bool>;
