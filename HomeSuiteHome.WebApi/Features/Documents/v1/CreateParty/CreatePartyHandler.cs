using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Domains.Parties;
using HomeSuiteHome.WebApi.Persistence;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.CreateParty;

public class CreatePartyHandler(AppDbContext db)
    : ICommandHandler<CreatePartyRequest, CreatePartyResponse>
{
    public async Task<CreatePartyResponse> HandleAsync(CreatePartyRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Party name cannot be empty.");

        var party = new Party
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = request.Description?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Parties.Add(party);
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
