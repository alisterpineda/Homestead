using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumable;

public class GetConsumableHandler(AppDbContext db, IItemLocationResolver locationResolver)
    : IRequestHandler<GetConsumableQuery, ConsumableDetailResponse?>
{
    public Task<ConsumableDetailResponse?> Handle(GetConsumableQuery request, CancellationToken ct) =>
        ConsumableDetailReader.LoadAsync(db, locationResolver, request.Id, ct);
}
