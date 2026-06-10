using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItem;

public class GetItemHandler(AppDbContext db)
    : IRequestHandler<GetItemQuery, ItemResponse?>
{
    public async Task<ItemResponse?> Handle(GetItemQuery request, CancellationToken ct)
    {
        return await db.Items
            .AsNoTracking()
            .Where(i => i.Id == request.Id)
            .Select(ItemResponseMapper.Projection)
            .FirstOrDefaultAsync(ct);
    }
}
