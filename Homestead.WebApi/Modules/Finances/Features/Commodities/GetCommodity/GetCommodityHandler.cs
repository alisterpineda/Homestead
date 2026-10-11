using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.GetCommodity;

public class GetCommodityHandler(AppDbContext db)
{
    public async Task<CommodityResponse?> HandleAsync(Guid id, CancellationToken ct)
    {
        return await db.Commodities
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(CommodityProjections.ToResponse(db))
            .FirstOrDefaultAsync(ct);
    }
}
