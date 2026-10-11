using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumable;

public class GetConsumableHandler(AppDbContext db, IItemLocationResolver locationResolver)
{
    public Task<ConsumableDetailResponse?> HandleAsync(Guid id, CancellationToken ct) =>
        ConsumableDetailReader.LoadAsync(db, locationResolver, id, ct);
}
