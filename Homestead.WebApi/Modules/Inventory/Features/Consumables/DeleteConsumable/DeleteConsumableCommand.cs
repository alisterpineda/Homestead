using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.DeleteConsumable;

public record DeleteConsumableCommand(Guid Id) : ICommand<bool>;
