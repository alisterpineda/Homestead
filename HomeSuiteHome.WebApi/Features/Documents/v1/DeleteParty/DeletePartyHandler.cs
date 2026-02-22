using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.DeleteParty;

public class DeletePartyHandler(AppDbContext db)
    : ICommandHandler<DeletePartyRequest, DeletePartyResponse>
{
    public async Task<DeletePartyResponse> HandleAsync(DeletePartyRequest request, CancellationToken ct)
    {
        var party = await db.Parties
            .FirstOrDefaultAsync(p => p.Id == request.Id, ct);

        if (party is null)
            return new DeletePartyResponse(false);

        db.Parties.Remove(party);
        await db.SaveChangesAsync(ct);

        return new DeletePartyResponse(true);
    }
}
