using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetTextExtractionEngineSummaries;

public class GetTextExtractionEngineSummariesHandler(AppDbContext db)
    : IQueryHandler<GetTextExtractionEngineSummariesRequest, IReadOnlyList<TextExtractionEngineSummary>>
{
    public async Task<IReadOnlyList<TextExtractionEngineSummary>> HandleAsync(
        GetTextExtractionEngineSummariesRequest request, CancellationToken ct)
    {
        var items = await db.TextExtractionEngines
            .AsNoTracking()
            .OrderBy(e => e.Name)
            .Select(e => new TextExtractionEngineSummary
            {
                Id = e.Id,
                Name = e.Name,
            })
            .ToListAsync(ct);

        return items;
    }
}
