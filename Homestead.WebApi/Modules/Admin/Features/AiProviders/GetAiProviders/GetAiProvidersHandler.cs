using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.GetAiProviders;

public class GetAiProvidersHandler(AppDbContext db)
    : IRequestHandler<GetAiProvidersQuery, PaginatedResponse<AiProviderResponse>>
{
    public async Task<PaginatedResponse<AiProviderResponse>> Handle(
        GetAiProvidersQuery request, CancellationToken ct)
    {
        var query = db.AiProviders.AsNoTracking().AsQueryable();
        var searchLower = request.Search?.Trim().ToLower();

        if (!string.IsNullOrWhiteSpace(searchLower))
        {
            query = query.Where(e => e.Name.ToLower().Contains(searchLower));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(e => e.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(e => new AiProviderResponse
            {
                Id = e.Id,
                Name = e.Name,
                ProviderType = e.ProviderType,
                Configuration = e.Configuration,
                IsEnabled = e.IsEnabled,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt,
                CreatedByUserId = e.CreatedByUserId,
                CreatedByUserName = e.CreatedByUser != null ? e.CreatedByUser.DisplayName : null,
            })
            .ToListAsync(ct);

        return new PaginatedResponse<AiProviderResponse>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount,
        };
    }
}
