using FluentValidation;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.GetTagsByIds;

public class GetTagsByIdsHandler(AppDbContext db, IValidator<GetTagsByIdsQuery> validator)
{
    public async Task<IReadOnlyList<GetTagsByIdsResponse>> HandleAsync(
        GetTagsByIdsQuery request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        return await db.Tags.AsNoTracking()
            .Where(t => request.Ids.Contains(t.Id))
            .Select(t => new GetTagsByIdsResponse
            {
                Id = t.Id,
                Path = t.Path,
            })
            .ToListAsync(ct);
    }
}
