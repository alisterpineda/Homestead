using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Consumables;
using Homestead.WebApi.Modules.Inventory.Features.Durables;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItem;

/// <summary>
/// Unified detail read: resolves the row's <see cref="Fungibility"/>, then branches to the existing
/// per-kind mapper/reader so the response shape exactly matches <c>GetDurable</c> / <c>GetConsumable</c>.
/// </summary>
public class GetItemHandler(AppDbContext db, IItemLocationResolver locationResolver)
{
    public async Task<ItemDetailResponse?> Handle(GetItemQuery request, CancellationToken ct)
    {
        // Project as a durable first: the projection is kind-agnostic and already carries Fungibility,
        // so the common durable case resolves in one query (like GetDurable) instead of a discriminator
        // probe plus a re-projection. Branch on the result; the consumable case re-reads via its reader.
        var durable = await db.Items
            .AsNoTracking()
            .Where(i => i.Id == request.Id)
            .Select(DurableResponseMapper.Projection)
            .FirstOrDefaultAsync(ct);

        if (durable is null)
            return null;

        if (durable.Fungibility == Fungibility.NonFungible)
        {
            return new ItemDetailResponse
            {
                Id = request.Id,
                Fungibility = Fungibility.NonFungible,
                Durable = await locationResolver.EnrichAsync(durable, ct),
            };
        }

        var consumable = await ConsumableDetailReader.LoadAsync(db, locationResolver, request.Id, ct);
        if (consumable is null)
            return null;

        return new ItemDetailResponse
        {
            Id = request.Id,
            Fungibility = Fungibility.Fungible,
            Consumable = consumable,
        };
    }
}
