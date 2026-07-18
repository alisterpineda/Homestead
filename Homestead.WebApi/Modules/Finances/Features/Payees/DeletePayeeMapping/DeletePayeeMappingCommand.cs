using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.DeletePayeeMapping;

public record DeletePayeeMappingCommand(Guid Id) : ICommand<bool>;
