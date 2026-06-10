using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItem;

public class GetItemHandler(AppDbContext db, IItemLocationResolver locationResolver)
    : IRequestHandler<GetItemQuery, ItemResponse?>
{
    public async Task<ItemResponse?> Handle(GetItemQuery request, CancellationToken ct)
    {
        var response = await db.Items
            .AsNoTracking()
            .Where(i => i.Id == request.Id)
            .Select(ItemResponseMapper.Projection)
            .FirstOrDefaultAsync(ct);

        return response is null ? null : await locationResolver.EnrichAsync(response, ct);
    }
}
