using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Domains.Parties;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.CreateParty;

public class CreatePartyHandler(AppDbContext db, ICurrentUserAccessor currentUser, IAuditLogger auditLogger)
    : IRequestHandler<CreatePartyCommand, CreatePartyResponse>
{
    public async Task<CreatePartyResponse> Handle(CreatePartyCommand request, CancellationToken ct)
    {
        var name = request.Name.Trim();

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
        auditLogger.LogCreated("Party", party.Id);
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
