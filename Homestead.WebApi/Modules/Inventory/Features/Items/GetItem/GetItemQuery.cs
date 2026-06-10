using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItem;

public record GetItemQuery : IRequest<ItemResponse?>
{
    public Guid Id { get; init; }
}
