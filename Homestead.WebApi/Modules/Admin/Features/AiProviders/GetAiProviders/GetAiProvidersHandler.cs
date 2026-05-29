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

        // DB-side projection keeps the join to a single CreatedByUser.DisplayName column (no full-entity
        // load); masking can't translate to SQL, so it runs in the in-memory pass below.
        var rows = await query
            .OrderBy(e => e.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.ProviderType,
                e.Configuration,
                e.IsEnabled,
                e.CreatedAt,
                e.UpdatedAt,
                e.CreatedByUserId,
                CreatedByUserName = e.CreatedByUser != null ? e.CreatedByUser.DisplayName : null,
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new AiProviderResponse
            {
                Id = r.Id,
                Name = r.Name,
                ProviderType = r.ProviderType,
                Configuration = JsonSecretConfig.Mask(r.Configuration, AiProviderSecrets.Keys),
                IsEnabled = r.IsEnabled,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                CreatedByUserId = r.CreatedByUserId,
                CreatedByUserName = r.CreatedByUserName,
            })
            .ToList();

        return new PaginatedResponse<AiProviderResponse>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount,
        };
    }
}
