using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.OcrEngines.GetOcrEngines;

public class GetOcrEnginesHandler(AppDbContext db)
    : IQueryHandler<GetOcrEnginesRequest, PaginatedResponse<OcrEngineResponse>>
{
    public async Task<PaginatedResponse<OcrEngineResponse>> HandleAsync(
        GetOcrEnginesRequest request, CancellationToken ct)
    {
        var builtIn = OcrEngineResponse.TesseractDefault;
        var searchLower = request.Search?.Trim().ToLower();
        var builtInMatchesSearch = string.IsNullOrWhiteSpace(searchLower)
            || builtIn.Name.ToLower().Contains(searchLower);

        var query = db.OcrEngines.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchLower))
        {
            query = query.Where(e => e.Name.ToLower().Contains(searchLower));
        }

        var dbTotalCount = await query.CountAsync(ct);
        var totalCount = dbTotalCount + (builtInMatchesSearch ? 1 : 0);

        // Built-in always appears first
        var items = new List<OcrEngineResponse>();

        if (builtInMatchesSearch && request.Page == 1)
        {
            items.Add(builtIn);
        }

        var dbItemsToTake = request.PageSize - items.Count;
        var dbSkip = request.Page == 1
            ? 0
            : (request.Page - 1) * request.PageSize - (builtInMatchesSearch ? 1 : 0);

        if (dbSkip < 0) dbSkip = 0;

        if (dbItemsToTake > 0)
        {
            var dbItems = await query
                .OrderBy(e => e.Name)
                .Skip(dbSkip)
                .Take(dbItemsToTake)
                .Select(e => new OcrEngineResponse
                {
                    Id = e.Id,
                    Name = e.Name,
                    EngineType = e.EngineType,
                    Configuration = e.Configuration,
                    IsBuiltIn = false,
                    CreatedAt = e.CreatedAt,
                    UpdatedAt = e.UpdatedAt,
                    CreatedByUserId = e.CreatedByUserId,
                    CreatedByUserName = e.CreatedByUser != null ? e.CreatedByUser.DisplayName : null,
                })
                .ToListAsync(ct);

            items.AddRange(dbItems);
        }

        return new PaginatedResponse<OcrEngineResponse>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount,
        };
    }
}
