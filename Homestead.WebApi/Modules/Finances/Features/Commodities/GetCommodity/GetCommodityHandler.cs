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
            .Select(c => new CommodityResponse
            {
                Id = c.Id,
                Symbol = c.Symbol,
                Name = c.Name,
                Kind = c.Kind,
                DisplayPrecision = c.DisplayPrecision,
            })
            .FirstOrDefaultAsync(ct);
    }
}
