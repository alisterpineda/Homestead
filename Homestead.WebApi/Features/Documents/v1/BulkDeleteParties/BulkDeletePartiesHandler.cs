using Homestead.WebApi.Authorization;
using Homestead.WebApi.Features.Auditing;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteParties;

public class BulkDeletePartiesHandler(AppDbContext db, IDocumentQueryService accessService, IAuditContext audit)
    : IRequestHandler<BulkDeletePartiesCommand, BulkDeletePartiesResponse>
{
    public async Task<BulkDeletePartiesResponse> Handle(BulkDeletePartiesCommand request, CancellationToken ct)
    {
        var parties = await db.Parties
            .Where(p => request.Ids.Contains(p.Id))
            .ToListAsync(ct);

        if (parties.Count == 0)
            return new BulkDeletePartiesResponse(0);

        // Block deletion if any parties are used on inaccessible documents
        var relatedDocIds = db.DocumentParties
            .Where(dp => request.Ids.Contains(dp.PartyId))
            .Select(dp => dp.DocumentId);

        await accessService.EnsureAllDocumentsWritableAsync(relatedDocIds, ct);

        foreach (var party in parties)
            audit.Deleted("Party", party.Id, description: party.Name);

        db.Parties.RemoveRange(parties);

        await db.SaveChangesAsync(ct);

        return new BulkDeletePartiesResponse(parties.Count);
    }
}
