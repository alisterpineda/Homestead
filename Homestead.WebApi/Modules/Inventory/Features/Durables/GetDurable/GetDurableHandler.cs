using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurable;

public class GetDurableHandler(AppDbContext db, IItemLocationResolver locationResolver)
    : IRequestHandler<GetDurableQuery, DurableResponse?>
{
    public async Task<DurableResponse?> Handle(GetDurableQuery request, CancellationToken ct)
    {
        var response = await db.Items
            .AsNoTracking()
            .Where(i => i.Id == request.Id)
            .Select(DurableResponseMapper.Projection)
            .FirstOrDefaultAsync(ct);

        return response is null ? null : await locationResolver.EnrichAsync(response, ct);
    }
}
