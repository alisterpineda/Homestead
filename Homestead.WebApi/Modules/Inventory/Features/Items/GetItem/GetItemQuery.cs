using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItem;

public record GetItemQuery(Guid Id) : IRequest<ItemDetailResponse?>;
