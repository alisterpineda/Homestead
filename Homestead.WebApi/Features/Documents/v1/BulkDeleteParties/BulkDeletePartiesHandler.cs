using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteParties;

public class BulkDeletePartiesHandler(AppDbContext db)
    : ICommandHandler<BulkDeletePartiesRequest, BulkDeletePartiesResponse>
{
    public async Task<BulkDeletePartiesResponse> HandleAsync(BulkDeletePartiesRequest request, CancellationToken ct)
    {
        var parties = await db.Parties
            .Where(p => request.Ids.Contains(p.Id))
            .ToListAsync(ct);

        if (parties.Count == 0)
            return new BulkDeletePartiesResponse(0);

        db.Parties.RemoveRange(parties);
        await db.SaveChangesAsync(ct);

        return new BulkDeletePartiesResponse(parties.Count);
    }
}
