using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.UpdateParty;

public class UpdatePartyHandler(AppDbContext db, IDocumentQueryService accessService)
    : ICommandHandler<UpdatePartyCommand, UpdatePartyResponse?>
{
    public async Task<UpdatePartyResponse?> HandleAsync(UpdatePartyCommand request, CancellationToken ct)
    {
        var party = await db.Parties
            .FirstOrDefaultAsync(p => p.Id == request.Id, ct);

        if (party is null)
            return null;

        // Block modification if the party is used on inaccessible documents
        var relatedDocIds = db.DocumentParties
            .Where(dp => dp.PartyId == request.Id)
            .Select(dp => dp.DocumentId);

        await accessService.EnsureAllDocumentsAccessibleAsync(relatedDocIds, ct);

        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Party name cannot be empty.");

        party.Name = name;
        party.Description = request.Description?.Trim();

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
