using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Settings;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.TextExtractionEngines.GetTextExtractionEngines;

public class GetTextExtractionEnginesHandler(AppDbContext db)
    : IRequestHandler<GetTextExtractionEnginesQuery, PaginatedResponse<TextExtractionEngineResponse>>
{
    public async Task<PaginatedResponse<TextExtractionEngineResponse>> Handle(
        GetTextExtractionEnginesQuery request, CancellationToken ct)
    {
        var builtIn = TextExtractionEngineResponse.TesseractDefault;
        var searchLower = request.Search?.Trim().ToLower();
        var builtInMatchesSearch = string.IsNullOrWhiteSpace(searchLower)
            || builtIn.Name.ToLower().Contains(searchLower);

        var query = db.TextExtractionEngines.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchLower))
        {
            query = query.Where(e => e.Name.ToLower().Contains(searchLower));
        }

        var dbTotalCount = await query.CountAsync(ct);
        var totalCount = dbTotalCount + (builtInMatchesSearch ? 1 : 0);

        // Built-in always appears first
        var items = new List<TextExtractionEngineResponse>();

        if (builtInMatchesSearch && request.EffectivePage == 1)
        {
            items.Add(builtIn);
        }

        var dbItemsToTake = request.EffectivePageSize - items.Count;
        var dbSkip = request.EffectivePage == 1
            ? 0
            : (request.EffectivePage - 1) * request.EffectivePageSize - (builtInMatchesSearch ? 1 : 0);

        if (dbSkip < 0) dbSkip = 0;

        if (dbItemsToTake > 0)
        {
            var dbItems = await query
                .OrderBy(e => e.Name)
                .Skip(dbSkip)
                .Take(dbItemsToTake)
                .Select(e => new TextExtractionEngineResponse
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

        return new PaginatedResponse<TextExtractionEngineResponse>
        {
            Items = items,
            Page = request.EffectivePage,
            PageSize = request.EffectivePageSize,
            TotalCount = totalCount,
        };
    }
}
