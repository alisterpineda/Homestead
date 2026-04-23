using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.GetCommodities;

public class GetCommoditiesHandler(AppDbContext db)
    : IRequestHandler<GetCommoditiesQuery, PaginatedResponse<CommodityResponse>>
{
    public async Task<PaginatedResponse<CommodityResponse>> Handle(
        GetCommoditiesQuery request, CancellationToken ct)
    {
        var query = db.Commodities.AsNoTracking();

        if (request.Kind is { } kind)
            query = query.Where(c => c.Kind == kind);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = $"%{request.Search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Symbol, pattern) ||
                EF.Functions.ILike(c.Name, pattern));
        }

        return await query
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.Symbol)
            .Select(c => new CommodityResponse
            {
                Id = c.Id,
                Symbol = c.Symbol,
                Name = c.Name,
                Kind = c.Kind,
                DisplayPrecision = c.DisplayPrecision,
            })
            .ToPaginatedAsync(request, ct);
    }
}
