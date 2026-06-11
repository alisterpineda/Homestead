using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.GetSupply;

public record GetSupplyQuery(Guid Id) : IRequest<SupplyDetailResponse?>;
