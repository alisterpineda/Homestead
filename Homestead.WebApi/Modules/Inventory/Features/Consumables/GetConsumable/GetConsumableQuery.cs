using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumable;

public record GetConsumableQuery(Guid Id) : IRequest<ConsumableDetailResponse?>;
