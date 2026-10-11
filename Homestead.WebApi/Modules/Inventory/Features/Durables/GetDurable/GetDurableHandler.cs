using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurable;

public class GetDurableHandler(AppDbContext db, IItemLocationResolver locationResolver)
{
    public async Task<DurableResponse?> HandleAsync(GetDurableQuery request, CancellationToken ct)
    {
        var response = await db.Items
            .AsNoTracking()
            .Where(i => i.Id == request.Id && i.Fungibility == Fungibility.NonFungible)
            .Select(DurableResponseMapper.Projection)
            .FirstOrDefaultAsync(ct);

        return response is null ? null : await locationResolver.EnrichAsync(response, ct);
    }
}
