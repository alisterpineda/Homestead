using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.CreateArea;

public record CreateAreaCommand : ICommand<AreaResponse>
{
    public required string Name { get; init; }
}
