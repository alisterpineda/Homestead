using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.GetBrands;

public class GetBrandsHandler(AppDbContext db)
    : IRequestHandler<GetBrandsQuery, PaginatedResponse<BrandResponse>>
{
    public async Task<PaginatedResponse<BrandResponse>> Handle(
        GetBrandsQuery request, CancellationToken ct)
    {
        var query = db.Brands.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(b => b.Name.ToLower().Contains(search));
        }

        var ordered = query.OrderBy(b => b.Name).Select(BrandResponseMapper.Projection);
        return await ordered.ToPaginatedAsync(request, ct);
    }
}
