using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.GetWarranties;

public class GetItemWarrantiesHandler(AppDbContext db, TimeProvider timeProvider)
    : IRequestHandler<GetItemWarrantiesQuery, IReadOnlyList<ItemWarrantyResponse>?>
{
    public async Task<IReadOnlyList<ItemWarrantyResponse>?> Handle(
        GetItemWarrantiesQuery request, CancellationToken ct)
    {
        // The route is on /items, so a consumable id is a legitimate lookup — it simply has no rows.
        // Only a missing item is a 404.
        var exists = await db.Items.AsNoTracking().AnyAsync(i => i.Id == request.Id, ct);
        if (!exists)
            return null;

        // Soonest expiry first, open-ended rows last: the "IS NULL" key is spelled out rather than
        // relying on Postgres's NULLS LAST default so the InMemory provider orders identically.
        var warranties = await db.ItemWarranties.AsNoTracking()
            .Where(w => w.ItemId == request.Id)
            .OrderBy(w => w.ExpiresOn == null)
            .ThenBy(w => w.ExpiresOn)
            .ThenBy(w => w.CreatedAt)
            .ThenBy(w => w.Id)
            .ToListAsync(ct);

        var today = WarrantyStatusRules.TodayUtc(timeProvider);
        return warranties.Select(w => ItemWarrantyResponseMapper.ToResponse(w, today)).ToList();
    }
}
