using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Parties;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.CreateParty;

public class CreatePartyHandler(AppDbContext db, ICurrentUserAccessor currentUser, IAuditLogger auditLogger)
    : ICommandHandler<CreatePartyRequest, CreatePartyResponse>
{
    public async Task<CreatePartyResponse> HandleAsync(CreatePartyRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
            throw new BadRequestException("Name is required.");

        var nameExists = await db.Parties.AnyAsync(p => p.Name == name, ct);
        if (nameExists)
            throw new ConflictException("A party with this name already exists.");

        var party = new Party
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = request.Description?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        db.Parties.Add(party);
        auditLogger.LogCreated("Party", party.Id, currentUser.UserId);
        await db.SaveChangesAsync(ct);

        return new CreatePartyResponse
        {
            Id = party.Id,
            Name = party.Name,
            Description = party.Description,
            CreatedAt = party.CreatedAt
        };
    }
}
