using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.DeleteSupply;

public record DeleteSupplyCommand(Guid Id) : ICommand<bool>;
