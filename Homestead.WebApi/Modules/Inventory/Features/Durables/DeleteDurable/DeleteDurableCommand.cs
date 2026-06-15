using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.DeleteDurable;

public record DeleteDurableCommand(Guid Id) : ICommand<bool>;
