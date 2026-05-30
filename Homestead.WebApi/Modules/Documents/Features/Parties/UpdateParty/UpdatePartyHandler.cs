using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Parties;
using Homestead.WebApi.Common.Auditing;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.UpdateParty;

public class UpdatePartyHandler(AppDbContext db, IDocumentQueryService accessService, IAuditContext audit,
    ICurrentUserAccessor currentUser, IUnitOfWork unitOfWork)
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
            throw new DuplicateEntityException("A party with this name already exists.");

        // Block modification if the party is used on inaccessible documents
        var relatedDocIds = db.DocumentParties
            .Where(dp => dp.PartyId == request.Id)
            .Select(dp => dp.DocumentId);

        await accessService.EnsureAllDocumentsAccessibleAsync(relatedDocIds, ct);

        audit.Track<PartySnapshot, Party>(party, "Party", party.Id);

        party.Name = name;
        party.Description = request.Description?.Trim();
        party.UpdatedAt = DateTimeOffset.UtcNow;
        party.UpdatedByUserId = currentUser.UserId;
        await unitOfWork.SaveChangesAsync(ct);

        // Load updater info
        string? updatedByUserName = null;
        if (party.UpdatedByUserId.HasValue)
        {
            updatedByUserName = await db.Users
                .Where(u => u.Id == party.UpdatedByUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);
        }

        return new UpdatePartyResponse
        {
            Id = party.Id,
            Name = party.Name,
            Description = party.Description,
            CreatedAt = party.CreatedAt,
            UpdatedAt = party.UpdatedAt,
            UpdatedByUserId = party.UpdatedByUserId,
            UpdatedByUserName = updatedByUserName
        };
    }
}
