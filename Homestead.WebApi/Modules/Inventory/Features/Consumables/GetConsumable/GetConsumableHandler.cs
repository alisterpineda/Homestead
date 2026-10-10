using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumable;

public class GetConsumableHandler(AppDbContext db, IItemLocationResolver locationResolver)
{
    public Task<ConsumableDetailResponse?> Handle(GetConsumableQuery request, CancellationToken ct) =>
        ConsumableDetailReader.LoadAsync(db, locationResolver, request.Id, ct);
}
