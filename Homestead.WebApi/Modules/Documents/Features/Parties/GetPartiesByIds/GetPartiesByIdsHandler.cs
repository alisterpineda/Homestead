using FluentValidation;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.GetPartiesByIds;

public class GetPartiesByIdsHandler(AppDbContext db, IValidator<GetPartiesByIdsQuery> validator)
{
    public async Task<IReadOnlyList<GetPartiesByIdsResponse>> HandleAsync(
        GetPartiesByIdsQuery request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        return await db.Parties.AsNoTracking()
            .Where(p => request.Ids.Contains(p.Id))
            .Select(p => new GetPartiesByIdsResponse
            {
                Id = p.Id,
                Name = p.Name,
            })
            .ToListAsync(ct);
    }
}
