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
            .ThenBy(c => c.Exchange)
            .Select(c => new CommodityResponse
            {
                Id = c.Id,
                Symbol = c.Symbol,
                Exchange = c.Exchange,
                Name = c.Name,
                Kind = c.Kind,
                DisplayPrecision = c.DisplayPrecision,
                IsSystemManaged = c.IsSystemManaged,
            })
            .ToPaginatedAsync(request, ct);
    }
}
