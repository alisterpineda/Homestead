using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.DeletePayee;

public record DeletePayeeCommand(Guid Id) : ICommand<bool>;
