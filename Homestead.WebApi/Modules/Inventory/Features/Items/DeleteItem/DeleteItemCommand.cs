using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.DeleteItem;

public record DeleteItemCommand(Guid Id) : ICommand<bool>;
