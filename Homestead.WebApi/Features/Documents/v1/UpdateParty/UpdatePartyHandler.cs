using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.UpdateParty;

public class UpdatePartyHandler(AppDbContext db, IDocumentQueryService accessService, IAuditLogger auditLogger)
    : IRequestHandler<UpdatePartyCommand, UpdatePartyResponse?>
{
    public async Task<UpdatePartyResponse?> Handle(UpdatePartyCommand request, CancellationToken ct)
    {
        var party = await db.Parties
            .FirstOrDefaultAsync(p => p.Id == request.Id, ct);

        if (party is null)
            return null;

        var name = request.Name.Trim();

        var nameConflict = await db.Parties.AnyAsync(p => p.Id != request.Id && p.Name == name, ct);
        if (nameConflict)
            throw new ConflictException("A party with this name already exists.");

        // Block modification if the party is used on inaccessible documents
        var relatedDocIds = db.DocumentParties
            .Where(dp => dp.PartyId == request.Id)
            .Select(dp => dp.DocumentId);

        await accessService.EnsureAllDocumentsAccessibleAsync(relatedDocIds, ct);

        var before = PartySnapshot.From(party);

        party.Name = name;
        party.Description = request.Description?.Trim();

        var after = PartySnapshot.From(party);
        var changes = before.Diff(after);
        auditLogger.LogUpdated("Party", party.Id, changes);

        await db.SaveChangesAsync(ct);

        return new UpdatePartyResponse
        {
            Id = party.Id,
            Name = party.Name,
            Description = party.Description,
            CreatedAt = party.CreatedAt
        };
    }
}
