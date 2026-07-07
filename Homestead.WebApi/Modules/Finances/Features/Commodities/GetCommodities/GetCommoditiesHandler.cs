using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Search;
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
            var pattern = $"%{LikeEscaper.Escape(request.Search.Trim())}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Symbol, pattern, LikeEscaper.EscapeChar) ||
                EF.Functions.ILike(c.Name, pattern, LikeEscaper.EscapeChar));
        }

        return await query
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.Symbol)
            .ThenBy(c => c.Exchange)
            .Select(CommodityProjections.ToResponse(db))
            .ToPaginatedAsync(request, ct);
    }
}
