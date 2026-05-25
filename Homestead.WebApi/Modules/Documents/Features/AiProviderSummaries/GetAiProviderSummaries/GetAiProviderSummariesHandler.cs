using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.AiProviderSummaries.GetAiProviderSummaries;

public class GetAiProviderSummariesHandler(AppDbContext db)
    : IRequestHandler<GetAiProviderSummariesQuery, IReadOnlyList<AiProviderSummary>>
{
    public async Task<IReadOnlyList<AiProviderSummary>> Handle(
        GetAiProviderSummariesQuery request, CancellationToken ct)
    {
        return await db.AiProviders
            .AsNoTracking()
            .OrderBy(e => e.Name)
            .Select(e => new AiProviderSummary
            {
                Id = e.Id,
                Name = e.Name,
                ProviderType = e.ProviderType,
                IsEnabled = e.IsEnabled,
            })
            .ToListAsync(ct);
    }
}
