using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.DeleteArea;

public record DeleteAreaCommand(Guid Id) : ICommand<bool>;
