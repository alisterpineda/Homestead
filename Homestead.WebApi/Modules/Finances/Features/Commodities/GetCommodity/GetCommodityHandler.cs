using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.GetCommodity;

public class GetCommodityHandler(AppDbContext db)
    : IRequestHandler<GetCommodityQuery, CommodityResponse?>
{
    public async Task<CommodityResponse?> Handle(GetCommodityQuery request, CancellationToken ct)
    {
        return await db.Commodities
            .AsNoTracking()
            .Where(c => c.Id == request.Id)
            .Select(CommodityProjections.ToResponse(db))
            .FirstOrDefaultAsync(ct);
    }
}
