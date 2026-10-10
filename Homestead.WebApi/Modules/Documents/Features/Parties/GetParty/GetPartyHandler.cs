using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.GetParty;

public class GetPartyHandler(AppDbContext db)
{
    public async Task<GetPartyResponse?> Handle(GetPartyQuery request, CancellationToken ct)
    {
        var query = db.Parties.AsNoTracking().Where(p => p.Id == request.Id);

        return await query
            .Select(p => new GetPartyResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                UpdatedByUserId = p.UpdatedByUserId,
                UpdatedByUserName = p.UpdatedByUser != null ? p.UpdatedByUser.DisplayName : null,
            })
            .FirstOrDefaultAsync(ct);
    }
}
