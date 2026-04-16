using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.TextExtractionEngineSummaries.GetTextExtractionEngineSummaries;

public class GetTextExtractionEngineSummariesHandler(AppDbContext db)
    : IRequestHandler<GetTextExtractionEngineSummariesQuery, IReadOnlyList<TextExtractionEngineSummary>>
{
    public async Task<IReadOnlyList<TextExtractionEngineSummary>> Handle(
        GetTextExtractionEngineSummariesQuery request, CancellationToken ct)
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
